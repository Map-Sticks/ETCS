using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ETCS.Commands
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class StartupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document found.";
                return Result.Failed;
            }
            Document doc = uiDoc.Document;
            View activeView = doc.ActiveView;
            if (activeView == null)
            {
                message = "No active view found.";
                return Result.Failed;
            }

            // Collect all FamilyInstance elements in the active view.
            FilteredElementCollector collector = new FilteredElementCollector(doc, activeView.Id)
                                                  .OfClass(typeof(FamilyInstance));
            List<FamilyInstance> familyInstances = collector.Cast<FamilyInstance>().ToList();

            // Group family instances by category.
            var categoryGroups = familyInstances
                                    .GroupBy(fi => fi.Category?.Name ?? "Unknown Category")
                                    .OrderBy(g => g.Key);

            // Create a new resizable WPF window.
            Window window = new Window
            {
                Title = "List of Family Instances",
                Width = 600,
                Height = 400,
                ResizeMode = ResizeMode.CanResize
            };

            // Use a ScrollViewer so content does not overflow.
            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            StackPanel mainPanel = new StackPanel();
            scrollViewer.Content = mainPanel;
            window.Content = scrollViewer;

            // Keep a list of the ListBoxes so that only one item is selected at a time.
            List<ListBox> allListBoxes = new List<ListBox>();

            foreach (var group in categoryGroups)
            {
                string categoryName = group.Key;
                int count = group.Count();

                // Create an expander for the category.
                Expander expander = new Expander
                {
                    Header = $"{categoryName} ({count} instance{(count > 1 ? "s" : "")})",
                    IsExpanded = false,
                    Margin = new Thickness(5)
                };

                // Create a ListBox for this group.
                ListBox listBox = new ListBox
                {
                    SelectionMode = SelectionMode.Single
                };
                allListBoxes.Add(listBox);

                // When the user selects an instance, clear other selections and update Revit’s selection.
                listBox.SelectionChanged += (sender, args) =>
                {
                    if (sender is ListBox lb && lb.SelectedItem is ListBoxItem selectedItem && selectedItem.Tag is ElementId eid)
                    {
                        foreach (var other in allListBoxes)
                        {
                            if (other != lb)
                                other.SelectedItem = null;
                        }
                        uiDoc.Selection.SetElementIds(new List<ElementId> { eid });
                    }
                };

                // Populate the list for this category group.
                foreach (FamilyInstance fi in group)
                {
                    string familyName = fi.Symbol?.Family?.Name ?? "Unknown Family";
                    string typeName = fi.Symbol?.Name ?? "Unknown Type";
                    string displayText = $"{familyName} - {typeName} (Id: {fi.Id.Value})";

                    ListBoxItem item = new ListBoxItem
                    {
                        Content = displayText,
                        Tag = fi.Id
                    };
                    listBox.Items.Add(item);
                }
                expander.Content = listBox;
                mainPanel.Children.Add(expander);
            }

            // Show the window modelessly so you may access Revit’s built-in Properties window.
            window.Show();

            return Result.Succeeded;
        }
    }
}