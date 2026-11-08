using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace ETCS
{
    /// <summary>
    /// Interaction logic for PanelCircuitNamingDialog.xaml
    /// </summary>
    public partial class PanelCircuitNamingDialog : Window
    {
        public PanelCircuitSettings? Settings { get; private set; }
        private Document _doc;

        public PanelCircuitNamingDialog(Document doc)
        {
            InitializeComponent();
            _doc = doc;
            LoadPanels();
        }

        private void LoadPanels()
        {
            // Retrieve electrical panel family instances from the active document.
            var collector = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType();

            cmbPanels.ItemsSource = collector;
            cmbPanels.DisplayMemberPath = "Name";
        }

        // New event handler that fires when the user selects a panel.
        private void cmbPanels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPanels.SelectedItem is Element selectedPanel)
            {
                // Attempt to retrieve the "First Circuit Number" parameter.
                Parameter param = selectedPanel.LookupParameter("First Circuit Number");
                int currentValue = 0;
                if (param != null && !param.IsReadOnly)
                {
                    currentValue = param.AsInteger();
                }
                txtFirstCircuitNumber.Text = currentValue.ToString();
            }
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            if (cmbPanels.SelectedItem == null)
            {
                MessageBox.Show("Please select a panel.");
                return;
            }
            Element? panel = cmbPanels.SelectedItem as Element;
            if (panel == null)
            {
                MessageBox.Show("Invalid panel selection.");
                return;
            }

            if (!int.TryParse(txtFirstCircuitNumber.Text, out int firstCircuitNumber))
            {
                MessageBox.Show("Enter a valid first circuit number.");
                return;
            }

            PanelCircuitSettings settings = new PanelCircuitSettings
            {
                PanelElement = panel,
                FirstCircuitNumber = firstCircuitNumber
            };

            Settings = settings;
            this.DialogResult = true;
            this.Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}