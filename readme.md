#SF Process Launcher

Launches and monitors processes and dependant processes.

So, for example if you have a business app, and whenever it's launched you need a  helper application launched simultaneously, this tool is for you.  

The benefit this tool brings over the typical batch file is that you can configure this tool to relaunch the helper app if it ever goes away (like the user accidentally closed it) AND it will take care of making sure the helper app is closed when the main business app closes.

See ProcessLauncherConfig.xml for an example configuration. The configuration file must be either next to the executable, or provided as the first argument in the form of a full path.

We made this app specifically to use in a Citrix published application environment.

SIDE NOTE: When you specify a "parent" app, it now watches for children that app launches and treats them all like "parent" apps.  This takes care of situations where we actually specify a launcher app of some sort that kicks off what you really want and then exits.
