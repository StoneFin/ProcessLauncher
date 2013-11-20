using System.Collections.Generic;
using System.Xml.Serialization;

namespace IDF.Utilities.ProcLaunch
{
  [XmlRoot("ProcessLauncher")]
  public class ProcTree
  {
    [XmlElement("ProcInfo")]
    public List<LaunchInfo> Processes { get; set; }
  }
}