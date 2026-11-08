using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.Attributes;
using System.Windows;
using SWC = System.Windows.Controls;  // Alias for WPF controls
using System.Windows.Interop;
using Autodesk.Revit.DB.Structure;

namespace ETCS.Commands
{
    // A simple class that holds a room (from a linked document) plus its parent link.
    public class SelectedRoomInfo
    {
        public RevitLinkInstance LinkInstance { get; }
        public Room Room { get; }
        public SelectedRoomInfo(RevitLinkInstance link, Room room)
        {
            LinkInstance = link;
            Room = room;
        }
    }

    // PARAMETERS CLASS
    public class FamilyPlacementParameters
    {
        public FamilySymbol SelectedFamilySymbol { get; set; } = default!;
        // Spacing values (converted internally to feet)
        public double SpacingX { get; set; }
        public double SpacingY { get; set; }
        // Elevation offset (in feet) – for non‑hosted families this becomes the absolute Z value.
        public double ElevationOffset { get; set; }
        // The user–selected level.
        public Level SelectedLevel { get; set; } = default!;
        // Indicates whether to place the family as hosted.
        public bool IsHostedFamily { get; set; }
    }

    // NEW: External Event Handler to run placement code in API context.
    public class PlaceInstancesHandler : IExternalEventHandler
    {
        public Document Doc { get; set; } = null!;
        public List<SelectedRoomInfo> SelectedRooms { get; set; } = new List<SelectedRoomInfo>();
        public FamilyPlacementParameters PlacementParams { get; set; } = null!;
        public int PlacedInstanceCount { get; private set; }

        public void Execute(UIApplication app)
        {
            try
            {
                using (Transaction tx = new Transaction(Doc, "Place Family Instances in Rooms"))
                {
                    tx.Start();
                    PlacedInstanceCount = 0;
                    foreach (SelectedRoomInfo roomInfo in SelectedRooms)
                    {
                        Room room = roomInfo.Room;
                        RevitLinkInstance link = roomInfo.LinkInstance;
                        Transform transform = link.GetTotalTransform();

                        SpatialElementBoundaryOptions opt = new SpatialElementBoundaryOptions();
                        IList<IList<BoundarySegment>>? boundaries = room.GetBoundarySegments(opt);
                        if (boundaries == null || boundaries.Count == 0)
                            continue;
                        IList<BoundarySegment> loop = boundaries.First();

                        List<XYZ> polygon = new List<XYZ>();
                        foreach (BoundarySegment seg in loop)
                        {
                            Curve? curve = seg.GetCurve();
                            if (curve != null)
                                polygon.Add(curve.GetEndPoint(0));
                        }
                        if (polygon.Count < 3)
                            continue;

                        double minX = polygon.Min(p => p.X);
                        double maxX = polygon.Max(p => p.X);
                        double minY = polygon.Min(p => p.Y);
                        double maxY = polygon.Max(p => p.Y);

                        double spacingX = PlacementParams.SpacingX;
                        double spacingY = PlacementParams.SpacingY;
                        List<XYZ> candidatePoints = new List<XYZ>();

                        for (double x = minX; x <= maxX; x += spacingX)
                        {
                            for (double y = minY; y <= maxY; y += spacingY)
                            {
                                XYZ pt = new XYZ(x, y, 0);
                                if (IsPointInsidePolygon(pt, polygon))
                                    candidatePoints.Add(pt);
                            }
                        }

                        // NEWCOLLISION: Compute centroid of the polygon.
                        XYZ centroid = ComputeCentroid(polygon);

                        // NEWCOLLISION: Determine final candidate point.
                        if (candidatePoints.Count == 0)
                        {
                            // No grid candidates; fallback to centroid.
                            candidatePoints.Add(centroid);
                        }
                        else
                        {
                            // Find the candidate point closest to the centroid.
                            XYZ closest = candidatePoints.OrderBy(pt => pt.DistanceTo(centroid)).First();
                            double d = closest.DistanceTo(centroid);
                            // If the point is too far from the center (greater than half spacingX), assume spacing is too large.
                            if (d > spacingX * 0.5)
                            {
                                candidatePoints.Clear();
                                candidatePoints.Add(centroid);
                            }
                            else
                            {
                                // Otherwise, use only the candidate point closest to the centroid.
                                candidatePoints.Clear();
                                candidatePoints.Add(closest);
                            }
                        }
                        // End NEWCOLLISION

                        List<XYZ> hostPoints = candidatePoints.Select(pt => transform.OfPoint(pt)).ToList();

                        if (PlacementParams.IsHostedFamily)
                        {
                            foreach (XYZ candidate in hostPoints)
                            {
                                Element? hostCeiling = FindCeilingHost(Doc, PlacementParams.SelectedLevel, candidate);
                                if (hostCeiling != null)
                                {
                                    Doc.Create.NewFamilyInstance(candidate, PlacementParams.SelectedFamilySymbol, hostCeiling,
                                        StructuralType.NonStructural);
                                    PlacedInstanceCount++;
                                }
                            }
                        }
                        else
                        {
                            double newZ = PlacementParams.ElevationOffset; // Absolute elevation
                            hostPoints = hostPoints.Select(pt => new XYZ(pt.X, pt.Y, newZ)).ToList();
                            foreach (XYZ pt in hostPoints)
                            {
                                Doc.Create.NewFamilyInstance(pt, PlacementParams.SelectedFamilySymbol, PlacementParams.SelectedLevel,
                                    StructuralType.NonStructural);
                                PlacedInstanceCount++;
                            }
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        public string GetName() => "PlaceInstancesHandler";

        // Existing helper: Ray-casting algorithm.
        private bool IsPointInsidePolygon(XYZ point, List<XYZ> polygon)
        {
            bool inside = false;
            int count = polygon.Count;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        // NEWCOLLISION: Compute the centroid of a polygon.
        private XYZ ComputeCentroid(List<XYZ> polygon)
        {
            double sumX = 0, sumY = 0, sumZ = 0;
            foreach (var pt in polygon)
            {
                sumX += pt.X;
                sumY += pt.Y;
                sumZ += pt.Z;
            }
            int count = polygon.Count;
            return new XYZ(sumX / count, sumY / count, sumZ / count);
        }

        // Existing helper: Finds a ceiling element on the given level that contains the point.
        private Element? FindCeilingHost(Document doc, Level level, XYZ point)
        {
            var ceilings = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .Where(c => c.LevelId == level.Id)
                .ToList();
            foreach (var ceiling in ceilings)
            {
                BoundingBoxXYZ? bbox = ceiling.get_BoundingBox(null);
                if (bbox != null)
                {
                    if (point.X >= bbox.Min.X && point.X <= bbox.Max.X &&
                        point.Y >= bbox.Min.Y && point.Y <= bbox.Max.Y)
                    {
                        return ceiling;
                    }
                }
            }
            return null;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class PlaceFamilyInstancesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }
            Document doc = uiDoc.Document;

            // PART 1: Room Selection (modal)
            List<SelectedRoomInfo>? selectedRooms = GetSelectedRooms(commandData);
            if (selectedRooms == null || selectedRooms.Count == 0)
            {
                TaskDialog.Show("Place Family Instances", "No rooms were selected.");
                return Result.Cancelled;
            }

            // PART 2: Show modeless parameter window that remains open after placement.
            ShowParameterWindow(doc, commandData, selectedRooms);

            // The external command finishes here so that the parameter window remains open.
            return Result.Succeeded;
        }

        /// <summary>
        /// Displays a modal window to select linked model rooms.
        /// </summary>
        private List<SelectedRoomInfo>? GetSelectedRooms(ExternalCommandData commandData)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            List<SelectedRoomInfo> allRooms = new List<SelectedRoomInfo>();

            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();
            foreach (RevitLinkInstance link in linkInstances)
            {
                Document? linkedDoc = link.GetLinkDocument();
                if (linkedDoc == null)
                    continue;
                var rooms = new FilteredElementCollector(linkedDoc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .OfClass(typeof(SpatialElement))
                    .Cast<Room>()
                    .ToList();
                foreach (Room room in rooms)
                {
                    allRooms.Add(new SelectedRoomInfo(link, room));
                }
            }

            Window roomWindow = new Window()
            {
                Title = "Select Linked Model Rooms",
                Width = 400,
                Height = 500,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            SWC.StackPanel panel = new SWC.StackPanel() { Margin = new Thickness(10) };
            SWC.TextBlock instruction = new SWC.TextBlock() { Text = "Select one or more rooms:" };
            panel.Children.Add(instruction);

            SWC.ListBox lb = new SWC.ListBox() { SelectionMode = SWC.SelectionMode.Multiple, Height = 350 };
            foreach (SelectedRoomInfo info in allRooms)
            {
                string linkName = info.LinkInstance.Name;
                string roomNumber = info.Room.Number;
                string roomName = info.Room.Name;
                string itemText = $"{linkName} - {roomNumber}: {roomName}";
                SWC.ListBoxItem item = new SWC.ListBoxItem() { Content = itemText, Tag = info };
                lb.Items.Add(item);
            }
            panel.Children.Add(lb);

            SWC.StackPanel btnPanel = new SWC.StackPanel() { Orientation = SWC.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            SWC.Button btnOK = new SWC.Button() { Content = "OK", Width = 80, Margin = new Thickness(5) };
            SWC.Button btnCancel = new SWC.Button() { Content = "Cancel", Width = 80, Margin = new Thickness(5) };
            btnPanel.Children.Add(btnOK);
            btnPanel.Children.Add(btnCancel);
            panel.Children.Add(btnPanel);

            roomWindow.Content = panel;
            WindowInteropHelper helper = new WindowInteropHelper(roomWindow);
            helper.Owner = commandData.Application.MainWindowHandle;

            List<SelectedRoomInfo> selectedRooms = new List<SelectedRoomInfo>();
            btnOK.Click += (s, e) =>
            {
                foreach (SWC.ListBoxItem item in lb.SelectedItems)
                {
                    if (item.Tag is SelectedRoomInfo info)
                        selectedRooms.Add(info);
                }
                roomWindow.DialogResult = true;
                roomWindow.Close();
            };
            btnCancel.Click += (s, e) =>
            {
                roomWindow.DialogResult = false;
                roomWindow.Close();
            };

            bool? result = roomWindow.ShowDialog();
            return result == true ? selectedRooms : null;
        }

        /// <summary>
        /// Displays a modeless parameter window that remains open after placement.
        /// When the user clicks "Place," the parameters are validated and an external event is raised.
        /// </summary>
        private void ShowParameterWindow(Document doc, ExternalCommandData commandData, List<SelectedRoomInfo> selectedRooms)
        {
            Window paramWindow = new Window()
            {
                Title = "Family Placement Parameters",
                Width = 400,
                Height = 450,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            SWC.StackPanel panel = new SWC.StackPanel() { Margin = new Thickness(10) };

            // Family Category and Type
            SWC.TextBlock lblCategory = new SWC.TextBlock() { Text = "Select Family Category:" };
            panel.Children.Add(lblCategory);
            SWC.ComboBox cmbCategory = new SWC.ComboBox() { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(cmbCategory);

            List<FamilySymbol> allSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Family?.FamilyCategory != null)
                .ToList();
            var groups = allSymbols.GroupBy(fs => fs.Family.FamilyCategory.Name)
                                   .OrderBy(g => g.Key)
                                   .ToDictionary(g => g.Key, g => g.ToList());
            foreach (string cat in groups.Keys)
            {
                cmbCategory.Items.Add(cat);
            }
            if (cmbCategory.Items.Count > 0)
                cmbCategory.SelectedIndex = 0;

            SWC.TextBlock lblType = new SWC.TextBlock() { Text = "Select Family Type:" };
            panel.Children.Add(lblType);
            SWC.ComboBox cmbType = new SWC.ComboBox() { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(cmbType);

            cmbCategory.SelectionChanged += (s, e) =>
            {
                cmbType.Items.Clear();
                if (cmbCategory.SelectedItem is string selCat && !string.IsNullOrEmpty(selCat))
                {
                    if (groups.TryGetValue(selCat, out List<FamilySymbol>? fsList))
                    {
                        foreach (FamilySymbol fs in fsList)
                        {
                            cmbType.Items.Add(fs.Name);
                        }
                        if (cmbType.Items.Count > 0)
                            cmbType.SelectedIndex = 0;
                    }
                }
            };
            cmbCategory.RaiseEvent(new SWC.SelectionChangedEventArgs(SWC.ComboBox.SelectionChangedEvent, new List<object>(), new List<object>()));

            // Level selection
            SWC.TextBlock lblLevel = new SWC.TextBlock() { Text = "Select Host Level:" };
            panel.Children.Add(lblLevel);
            SWC.ComboBox cmbLevel = new SWC.ComboBox() { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(cmbLevel);

            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            foreach (Level lev in levels)
            {
                cmbLevel.Items.Add($"{lev.Name} (Elev: {Math.Round(lev.Elevation * 304.8, 0)} mm)");
            }
            if (cmbLevel.Items.Count > 0)
                cmbLevel.SelectedIndex = 0;

            // Spacing values (mm)
            SWC.TextBlock lblX = new SWC.TextBlock() { Text = "X Spacing (mm):" };
            panel.Children.Add(lblX);
            SWC.TextBox txtX = new SWC.TextBox() { Text = "500.0", Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(txtX);

            SWC.TextBlock lblY = new SWC.TextBlock() { Text = "Y Spacing (mm):" };
            panel.Children.Add(lblY);
            SWC.TextBox txtY = new SWC.TextBox() { Text = "500.0", Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(txtY);

            // Elevation Offset (mm)
            SWC.TextBlock lblElevation = new SWC.TextBlock() { Text = "Elevation Offset (mm):" };
            panel.Children.Add(lblElevation);
            SWC.TextBox txtElevation = new SWC.TextBox() { Text = "0.0", Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(txtElevation);

            // Hosted Family checkbox
            SWC.CheckBox chkHosted = new SWC.CheckBox() { Content = "Place as Hosted Family", Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(chkHosted);

            // Buttons Panel
            SWC.StackPanel btnPanel = new SWC.StackPanel() { Orientation = SWC.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            SWC.Button btnPlace = new SWC.Button() { Content = "Place", Width = 80, Margin = new Thickness(5) };
            SWC.Button btnCancel = new SWC.Button() { Content = "Cancel", Width = 80, Margin = new Thickness(5) };
            btnPanel.Children.Add(btnPlace);
            btnPanel.Children.Add(btnCancel);
            panel.Children.Add(btnPanel);

            paramWindow.Content = panel;
            WindowInteropHelper helper2 = new WindowInteropHelper(paramWindow);
            helper2.Owner = commandData.Application.MainWindowHandle;

            // NEW: Create an external event handler and external event.
            PlaceInstancesHandler handler = new PlaceInstancesHandler();
            handler.Doc = doc;
            handler.SelectedRooms = selectedRooms;
            ExternalEvent extEvent = ExternalEvent.Create(handler);

            // Show the parameter window modelessly.
            paramWindow.Show();

            // "Place" button click event: Validate inputs and raise the external event.
            btnPlace.Click += (s, e) =>
            {
                try
                {
                    if (!(cmbCategory.SelectedItem is string selCat) || string.IsNullOrEmpty(selCat))
                        throw new Exception("Family category not selected.");
                    if (!groups.TryGetValue(selCat, out List<FamilySymbol>? fsList))
                        throw new Exception("Invalid family category selection.");
                    if (!(cmbType.SelectedItem is string selType) || string.IsNullOrEmpty(selType))
                        throw new Exception("Invalid family type selection.");
                    FamilySymbol? chosen = fsList.FirstOrDefault(fs => fs.Name == selType);
                    if (chosen == null)
                        throw new Exception("Family type not found.");

                    double spacingX = double.Parse(txtX.Text) / 304.8;
                    double spacingY = double.Parse(txtY.Text) / 304.8;
                    double elevationOffset = double.Parse(txtElevation.Text) / 304.8;
                    int levelIndex = cmbLevel.SelectedIndex;
                    if (levelIndex < 0 || levelIndex >= levels.Count)
                        throw new Exception("Invalid level selection.");
                    Level selLevel = levels[levelIndex];
                    bool isHosted = (chkHosted.IsChecked == true);

                    FamilyPlacementParameters placementParams = new FamilyPlacementParameters()
                    {
                        SelectedFamilySymbol = chosen,
                        SpacingX = spacingX,
                        SpacingY = spacingY,
                        ElevationOffset = elevationOffset,
                        SelectedLevel = selLevel,
                        IsHostedFamily = isHosted
                    };

                    // Pass the parameters to the external event handler and raise the event.
                    handler.PlacementParams = placementParams;
                    extEvent.Raise();

                    // After the event is executed, show a notification with the count.
                    TaskDialog.Show("Placement Complete", $"Family instances placed: {handler.PlacedInstanceCount}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Input error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            // For Cancel, simply hide the window.
            btnCancel.Click += (s, e) =>
            {
                paramWindow.Hide();
            };
        }
    }
}