using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Linq;
using System.Threading;

namespace IDF.Utilities.ProcLaunch
{
  public class ProcessLauncher
  {
    private static string WorkingDirectory { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
    private static ProcTree _configInfo;

    private static void Main(string[] args)
    {
      try
      {
        //the config file is either the first argument, or right next to the assembly
        string configPath;
        if (args.Length != 0 && File.Exists(args[0]))
          configPath = args[0];
        else
          configPath = Path.Combine(WorkingDirectory, "ProcessLauncherConfig.xml");
        //load config
        var reader = File.OpenRead(configPath);
        _configInfo = (new System.Xml.Serialization.XmlSerializer(typeof(ProcTree))).Deserialize(reader) as ProcTree;
      }
      catch (Exception ex)
      {
        Exception InnerException = ex.InnerException;
        while (InnerException.InnerException != null)
          InnerException = InnerException.InnerException;
        Console.WriteLine("Invalid config - caught error messsage: " + ex.Message);
        Console.WriteLine(InnerException.Message);
        Console.WriteLine("");
        Console.WriteLine("The config file must be either next to ProcessLauncher.exe, and named ProcessLauncher.config, OR specified by a full path as the first argument to ProcessLauncher.exe");
        Console.WriteLine("Press any key to exit");
        Console.ReadKey();
        return;
      }


      //run through the config tree and set up all the process infos.  Don't launch them yet.
      foreach (var procToLaunch in _configInfo.Processes)
      {
        try
        {
          SetupProcess(procToLaunch);
        }
        catch (Exception ex)
        {
          Console.WriteLine(string.Format("Error setting up parent process '{0}'{1}{2}",procToLaunch.Path,Environment.NewLine,ex.Message));
          Console.WriteLine("Press any key to continue");
          Console.ReadKey();
          return;
        }
        foreach (var childDep in procToLaunch.Dependencies)
        {
          try
          {
            childDep.Parent = procToLaunch;
            SetupProcess(childDep);
          }
          catch (Exception ex)
          {
            Console.WriteLine(string.Format("Error setting up child process '{0}'{1}{2}", childDep.Path, Environment.NewLine, ex.Message));
            Console.WriteLine("Press any key to continue");
            Console.Read();
            return;
          }
        }
      }

      //start the processes now
      lock (_configInfo)
      {
        foreach (var proc in _configInfo.Processes)
        {
          //kick off a parent
          proc.ProcessInfo.Start();

          foreach (var childProc in proc.Dependencies)
          {
            //kick off all the children for this parent
            Thread.Sleep(100); //wait a little bit in between child processes
            childProc.ProcessInfo.Start();
          }
        }
      }

      //give the programs a couple seconds to spin up. This should probably be configurable
      Thread.Sleep(2500);

      //now loop until all the parent processes are killed. Then terminate their children. Hasta la vista, baby. 
      while (true)
      {
        //do we kill the program yet?
        lock (_configInfo)
        {
          if (_configInfo.Processes.All(x => x.ProcessInfo.HasExited))
          {
            //we want to watch for child spawns - if we run this thing and it spawns a child, and then dies (because it's another helper app), we want to pay attention to it's child and treat it as the "parent"
            var parentsHaveLiveChildren = false;
            foreach (var launchInfo in _configInfo.Processes)
            {
              //the parent process is dead at this point, but we know what it's processID was.
              var children = ProcessExtensions.GetChildProcesses(launchInfo.ProcessInfo.Id);
              if (children.Count() > 0)
              {
                //then we have spawns and should not exit
                parentsHaveLiveChildren = true;
                break;
              }
            }


            if (!parentsHaveLiveChildren)
            {
              //kill all dependencies
              foreach (
                var childProc in
                  _configInfo.Processes.SelectMany(proc => proc.Dependencies.Where(x => !x.ProcessInfo.HasExited)))
              {
                childProc.ProcessInfo.EnableRaisingEvents = false;
                childProc.ProcessInfo.Kill();
              }
              Environment.Exit(0);
            }
          }
        }
        Thread.Sleep(5000);
      }
    }

    #region Helpers
    public static class ProcessExtensions
    {
      /// <summary>
      /// Static method to get a list of child processes for a specific process ID
      /// </summary>
      /// <param name="processId">Id of the parent process</param>
      /// <returns>IEnumerable list of processes that have a "parent ID" attribute that matches that ID passed in</returns>
      public static IEnumerable<Process> GetChildProcesses(int processId)
      {
        var children = new List<Process>();
        var mos = new ManagementObjectSearcher(String.Format("Select * From Win32_Process Where ParentProcessID={0}", processId));

        foreach (ManagementObject mo in mos.Get())
        {
          children.Add(Process.GetProcessById(Convert.ToInt32(mo["ProcessID"])));
        }

        return children;
      }
    }
    private static void SetupProcess(ProcInfo dep)
    {
      if (!File.Exists(dep.Path))
        throw new ArgumentException("You must specify a valid path for all processes!",
                                    new FileNotFoundException("File not found: " + dep.Path));
      if (!String.IsNullOrEmpty(dep.WorkingDir) && !Directory.Exists(dep.WorkingDir))
        throw new ArgumentException("You must specify a valid working directory or none at all for all processes!",
                                    new DirectoryNotFoundException("Directory not found: " + dep.WorkingDir));

      //fire up the main process, then fire up the dependencies.
      dep.ProcessInfo = new System.Diagnostics.Process
                          {
                            StartInfo = new ProcessStartInfo(dep.Path, dep.Arguments ?? "")
                                          {
                                            WorkingDirectory =
                                              String.IsNullOrEmpty(dep.WorkingDir)
                                                ? WorkingDirectory
                                                : dep.WorkingDir
                                          },
                            EnableRaisingEvents = true
                          };
      //compiler doesn't like method groups as return values of expressions, so don't use ternary here!
      if (dep.Parent == null)
        dep.ProcessInfo.Exited += parentProcess_Exited;
      else
        dep.ProcessInfo.Exited += depProcess_Exited;
    }
    #endregion

    #region Event Handlers
    static void parentProcess_Exited(object sender, EventArgs e)
    {
      //a parent process has exited, kill all dependent programs!
      lock (_configInfo)
      {
        var proc = _configInfo.Processes.Single(x => x.ProcessInfo == (Process)sender);
        foreach (var dep in proc.Dependencies.Where(x => !x.ProcessInfo.HasExited))
          dep.ProcessInfo.Kill();
      }
    }

    static void depProcess_Exited(object sender, EventArgs e)
    {
      lock (_configInfo)
      {
        var theDep = _configInfo.Processes.SelectMany(x => x.Dependencies).Single(x => x.ProcessInfo == (Process)sender);
        //if the parent is done, then don't do anything. If not, and if the process supposed to be restarted, restart it.
        if (theDep.Restart && theDep.ProcessInfo.HasExited) //Typically, the subject being copied is terminated. 
          theDep.ProcessInfo.Start(); //start just re-uses the ProcessStartInfo and starts a new pid with the same info
      }
    }
    #endregion
  }
}
