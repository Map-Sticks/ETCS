using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
// Add reference to Microsoft.VisualBasic.dll to resolve the Interaction namespace.
using Microsoft.VisualBasic;
using Autodesk.Revit.DB.Electrical;

namespace ETCS.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class UpdatePanelScheduleCircuitNamingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                // Prompt the user to input a starting circuit number.
                string input = Microsoft.VisualBasic.Interaction.InputBox(
    "Enter the starting circuit number:",
    "Custom Starting Circuit Number",
    "1");

                if (!int.TryParse(input, out int startingNumber))
                {
                    TaskDialog.Show("Invalid Input", "Please enter a valid integer for the starting number.");
                    return Result.Failed;
                }

                // Let the user select one or more panel elements.
                ICollection<ElementId> selectedPanelIds = uiDoc.Selection.GetElementIds();
                if (selectedPanelIds.Count == 0)
                {
                    TaskDialog.Show("Selection Error", "Please select at least one panel.");
                    return Result.Failed;
                }

                using (Transaction trans = new Transaction(doc, "Update Panel Schedule Circuit Naming"))
                {
                    trans.Start();

                    foreach (ElementId panelId in selectedPanelIds)
                    {
                        Element panel = doc.GetElement(panelId);
                        if (panel == null)
                            continue;

                        // Retrieve circuits associated with this panel.
                        List<Element> circuits = GetCircuitsFromPanel(panel, doc);

                        int circuitNumber = startingNumber;
                        foreach (Element circuit in circuits)
                        {
                            // Look up the custom shared parameter (ensure it exists and is editable)
                            Parameter circuitParam = circuit.LookupParameter("Custom Circuit Number");
                            if (circuitParam != null && !circuitParam.IsReadOnly)
                            {
                                circuitParam.Set(circuitNumber);
                                circuitNumber++;
                            }
                            else
                            {
                                // Optionally, log a warning if the parameter is missing or read-only.
                                TaskDialog.Show("Warning", $"Parameter 'Custom Circuit Number' not found or read-only on element ID: {circuit.Id}");
                            }
                        }
                    }

                    trans.Commit();
                }

                TaskDialog.Show("Success", "Panel schedule circuits updated successfully.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Retrieves electrical circuits associated with a given panel.
        /// This example assumes that circuits are represented as ElectricalSystem elements,
        /// and that they reference a panel via a parameter named "PANEL_ID_PARAM".
        /// </summary>
        /// <param name="panel">The selected panel element.</param>
        /// <param name="doc">The current document.</param>
        /// <returns>A list of circuit elements linked to the panel.</returns>
        private List<Element> GetCircuitsFromPanel(Element panel, Document doc)
        {
            List<Element> circuits = new List<Element>();

            // Collect ElectricalSystem elements (typically representing circuits).
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem));

            foreach (Element elem in collector)
            {
                ElectricalSystem elecSys = elem as ElectricalSystem;
                if (elecSys == null)
                    continue;

                // Retrieve the parameter that links the circuit to a panel.
                Parameter panelParam = elecSys.LookupParameter("PANEL_ID_PARAM");
                if (panelParam != null &&
                    panelParam.StorageType == StorageType.ElementId &&
                    panelParam.AsElementId() == panel.Id)
                {
                    circuits.Add(elecSys);
                }
            }

            return circuits;
        }
    }
}