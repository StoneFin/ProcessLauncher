using System.Collections.Generic;
using System.Xml.Serialization;

namespace IDF.Utilities.ProcLaunch
{
  public class LaunchInfo : ProcInfo
  {
    [XmlElement("ChildProcInfo")]
    public List<ProcInfo> Dependencies { get; set; }
  }
}