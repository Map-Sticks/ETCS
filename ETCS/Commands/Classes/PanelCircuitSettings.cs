using Autodesk.Revit.DB;

namespace ETCS
{
    public class PanelCircuitSettings
    {
        public Element PanelElement { get; set; } = null!;
        public int FirstCircuitNumber { get; set; }
    }
}