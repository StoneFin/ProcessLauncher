using System.Diagnostics;
using System.Xml.Serialization;

namespace IDF.Utilities.ProcLaunch
{
  public class ProcInfo
  {
    [XmlAttribute]
    public string Path { get; set; }
    [XmlAttribute]
    public string WorkingDir { get; set; }
    [XmlAttribute]
    public string Arguments { get; set; }
    [XmlAttribute]
    public bool Restart { get; set; }

    [XmlIgnore]
    public Process ProcessInfo;
    [XmlIgnore]
    public ProcInfo Parent { get; set; }
  }
}