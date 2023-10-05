using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VMS.TPS;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace DoseComparison
{

    /// <summary>
    /// The PlanMenu class contains the interaction methods for PlanMenu.xaml. The bulk of the plan calculation
    /// is performed in the ComparePlans() method.
    /// </summary>
    /// <param name="context">context contains the API handle to the patient currently open in Eclipse</param>
    public partial class PlanMenu : UserControl
    {

        public ScriptContext context;

        // PlanMenu constructor
        public PlanMenu()
        {
            InitializeComponent();
        }

        // FillPlansComboBoxes is called by the PlanMenu parent once launched and populates the reference and 
        // target plan dropdown menus with a list of the patient plans opened in the specified course.
        public void FillPlansComboBoxes(Course c)
        {
            // Loop through each plan in the current patient course
            foreach (PlanSetup p in c.PlanSetups)
            {
                // Add plan name to the reference combo box list
                uiReferenceList.Items.Add(p);

                // Add plan name to the target combo box list
                uiTargetList.Items.Add(p);
            }
        }

        // SetDTA is called when the reference plan is changed in the drop down menu, and sets the DTA input box
        // to the dose resolution of the refrence plan. Note that this field is not editable, as the Gamma
        // calculation technique in this class is not robust enough to support DTA values that differ significantly
        // from one voxel size.
        private void SetDTA(object sender, SelectionChangedEventArgs e)
        {
            // Loop through each plan in the current patient course
            foreach (PlanSetup p in context.Course.PlanSetups)
            {
                // If the current plan matches the reference drop down value
                if (p.Id == uiReferenceList.SelectedValue.ToString())
                {
                    // Set the DTA input field to the plan dose resolution and append units. Note, this picks largest
                    // of the three (X,Y,Z) dimensions.
                    uiDTA.Text = Math.Max(Math.Max(p.Dose.XRes, p.Dose.YRes), p.Dose.ZRes).ToString() + " mm";
                }
            }

            // Clear the results fields, since the reference plan and/or DTA may have changed
            uiPassRate.Text = "";
            uiMaxDiff.Text = "";
        }

        // ClearResults is called when the target plan drop down menu is changed, to clear the results fields
        private void ClearResults(object sender, SelectionChangedEventArgs e)
        {
            uiPassRate.Text = "";
            uiMaxDiff.Text = "";
        }

        // ValidateThreshold is called whenever the threshold input field is changed, to validate and reformat the input
        private void ValidateThreshold(object sender, KeyboardFocusChangedEventArgs e)
        {
            // First try to parse the input text as a number (the regex automatically removes the units)
            Double.TryParse(Regex.Match(uiThreshold.Text, @"\d+\.*\d*").Value, out double test);
            if (test > 0)
            {
                // If successful, store the parsed number to single decimal precision with the dose units
                test = Math.Round(test * 10) / 10;
                uiThreshold.Text = test.ToString() + " Gy";
            }
            else
                // If not successful, revert the field to the default value
                uiThreshold.Text = "0.1 Gy";

            // Clear the results fields assuming the threshold was changed
            uiPassRate.Text = "";
            uiMaxDiff.Text = "";
        }

        // ValidatePercent is called whenever the Gamma percent input field is changed, to validate and reformat the input
        private void ValidatePercent(object sender, KeyboardFocusChangedEventArgs e)
        {
            // First try to parse the input text as a number (the regex automatically removes the % symbol)
            Double.TryParse(Regex.Match(uiPercent.Text, @"\d+\.*\d*").Value, out double test);
            if (test > 0)
            {
                // If successful, store the parsed number to single decimal precision with a percent symbol
                test = Math.Round(test * 10) / 10;
                uiPercent.Text = test.ToString() + "%";
            }
            else
                // If not successful, revert the field to the default value
                uiPercent.Text = "1.0%";

            // Clear the results fields assuming the percent was changed
            uiPassRate.Text = "";
            uiMaxDiff.Text = "";

        }

        // ComparePlans is called when the form button is clicked, and compares the dose volumes of the two plans. 
        // The results are reported back to the UI and a message box is displayed.
        private void ComparePlans(object sender, RoutedEventArgs e)
        {
            // Initialize variables to store the currently selected target and reference plan API handles
            PlanSetup selectedTarget = null;
            PlanSetup selectedReference = null;

            // Loop through each plan in the current patient course and store the matching plan API handle
            foreach (PlanSetup p in context.Course.PlanSetups)
            {
                if (p.Id == uiTargetList.SelectedValue.ToString())
                {
                    selectedTarget = p;
                }
                if (p.Id == uiReferenceList.SelectedValue.ToString())
                {
                    selectedReference = p;
                }
            }

            // Verify/set the dose values for both plans to absolute dose
            selectedTarget.DoseValuePresentation = DoseValuePresentation.Absolute;
            selectedReference.DoseValuePresentation = DoseValuePresentation.Absolute;

            // Verify dose dimensions and origins are the same. This is a requirement for this function, as it does not
            // currently contain the capability to interpolate the target dose into the reference dose. This is okay for the
            // default use of this tool, which is to recalculate the same plan (post-upgrade or beam model change) to assess
            // whether the plan has significantly changed.
            if (selectedTarget.Dose.XSize != selectedReference.Dose.XSize ||
                selectedTarget.Dose.YSize != selectedReference.Dose.YSize ||
                selectedTarget.Dose.ZSize != selectedReference.Dose.ZSize ||
                !selectedTarget.Dose.Origin.Equals(selectedReference.Dose.Origin))
            {
                // If the volumes are not exact, display an error message and end execution
                MessageBox.Show("ERROR: the two dose volumes must be the same");
                return;
            }

            // Retrieve the gamma parameters from the UI. Note, the Gamma percentage is stored as a fraction
            double gammaFrac = Double.Parse(Regex.Match(uiPercent.Text, @"\d+\.*\d*").Value) / 100;
            double gammaDTA = Double.Parse(Regex.Match(uiDTA.Text, @"\d+\.*\d*").Value);
            double threshold = Double.Parse(Regex.Match(uiThreshold.Text, @"\d+\.*\d*").Value);

            // Call CalculateGamma with these inputs
            double[] results = Script.CalculateGamma(selectedReference.Dose, selectedTarget.Dose, gammaFrac, gammaDTA, threshold);

            // Update the UI with the results
            uiPassRate.Text = results[0].ToString() + "%";
            uiMaxDiff.Text = results[1].ToString() + " Gy (" + results[2].ToString() + "%)";

            // Display a message box to the user indicating that calculations have completed
            MessageBox.Show("Calcuations completed");
        }
    }
}
