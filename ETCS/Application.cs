using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System.Reflection;
using ETCS.Commands; // Ensure this using statement locates StartupCommand, PlaceFamilyInstancesCommand and UpdatePanelScheduleCircuitNamingCommand

namespace ETCS
{
    public class Application : IExternalApplication
    {
        public Result OnShutdown(UIControlledApplication application)
        {
            // Perform any necessary cleanup here.
            return Result.Succeeded;
        }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // Create a new Ribbon tab if needed.
                string tabName = "ETCS Tools";
                application.CreateRibbonTab(tabName);

                // Create a ribbon panel where all buttons will reside.
                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Commands");

                // Get the current assembly path (the project DLL that contains our commands).
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                // -----------------------------
                // First Button: List Family Instances
                // -----------------------------
                PushButtonData listButtonData = new PushButtonData(
                    "ListFamilyInstancesButton",                     // Internal name.
                    "List Family Instances",                         // Button text.
                    assemblyPath,
                    typeof(StartupCommand).FullName);                // Fully qualified command name.
                listButtonData.ToolTip = "List all family instances in the current view in a resizable window.";
                panel.AddItem(listButtonData);

                // -----------------------------
                // Second Button: Place Family Instances Based on Linked Rooms
                // -----------------------------
                PushButtonData placeButtonData = new PushButtonData(
                    "PlaceFamilyInstancesButton",
                    "Place Family Instances",
                    assemblyPath,
                    typeof(PlaceFamilyInstancesCommand).FullName);   // Fully qualified command name.
                placeButtonData.ToolTip = "Automatically place a family instance (or grid of instances) based on linked model rooms.";
                panel.AddItem(placeButtonData);

                // -----------------------------
                // Third Button: Update Panel Schedule Circuit Naming
                // -----------------------------
                PushButtonData updateButtonData = new PushButtonData(
                    "UpdatePanelScheduleCircuitNamingButton",
                    "Update Panel Schedule Circuit Naming",
                    assemblyPath,
                    typeof(UpdatePanelScheduleCircuitNamingCommand).FullName); // Fully qualified command name.
                updateButtonData.ToolTip = "Update the panel schedules with the custom circuit naming as per user settings.";
                panel.AddItem(updateButtonData);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error during startup", ex.Message);
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}