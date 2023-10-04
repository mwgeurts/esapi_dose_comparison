using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

            // Initialize the results statistics that will be used/displayed at the end of this computation
            double maxDiff = 0;
            double maxRef = 0;
            int gammaPass = 0;
            int gammaFail = 0;

            // Calculate the scale and offset values (stored in the first two dose values). This is needed because the GetVoxels()
            // method employed below returns integer dose values, which must be converted back to dose by the scale/offset. It is 
            // computationally faster to store these and apply the scaling/offset manually rather than call VoxelToDoseValue() on 
            // every single voxel in both plans.
            double tscale = selectedTarget.Dose.VoxelToDoseValue(1).Dose - selectedTarget.Dose.VoxelToDoseValue(0).Dose;
            double toffset = selectedTarget.Dose.VoxelToDoseValue(0).Dose / tscale;
            double rscale = selectedReference.Dose.VoxelToDoseValue(1).Dose - selectedReference.Dose.VoxelToDoseValue(0).Dose;
            double roffset = selectedReference.Dose.VoxelToDoseValue(0).Dose / rscale;

            // Initialize temporary variables that will be used during the for loops
            double refval = 0;
            double diff = 0;

            // Initialize 2D arrays that will be reused during the for loops. Note there are three 2D planes that will be retrieved
            // From the target dose volume each iteration; this is to allow the gamma calculation to be 3-dimensional. This is also 
            // why DTA must be restricted.
            int[,] rarray = new int[selectedReference.Dose.XSize, selectedReference.Dose.YSize];
            int[,] t0array = new int[selectedTarget.Dose.XSize, selectedTarget.Dose.YSize];
            int[,] t1array = new int[selectedTarget.Dose.XSize, selectedTarget.Dose.YSize];
            int[,] t2array = new int[selectedTarget.Dose.XSize, selectedTarget.Dose.YSize];

            // Loop through each planar dose in the Z dimension. Note, these for loops exclude the edges (starting at 1, ending at
            // size-1), as each neighboring plane is included in the DTA search. This assumes that the edge voxels are all below 
            // The threshold dose, otherwise the accuracy may be impacted. That said, it would be relatively straightforward to add 
            // the additional bounds checks to the if statements below. 
            for (int z = 1; z < selectedReference.Dose.ZSize - 1; z++)
            {
                // Retrieve the current reference dose plane index
                selectedReference.Dose.GetVoxels(z, rarray);

                // If the entire dose plane is less than the dose threshold, skip ahead now and save some computation time
                if (rscale * rarray.Cast<int>().Max() + roffset < threshold)
                    continue;

                // Retrieve the previous, current, and next adjacent target dose planes
                selectedTarget.Dose.GetVoxels(z - 1, t0array);
                selectedTarget.Dose.GetVoxels(z, t1array);
                selectedTarget.Dose.GetVoxels(z + 1, t2array);

                // Loop through each voxel of the 2D plane. See above note about excluding the edge values.
                for (int y = 1; y < selectedReference.Dose.YSize - 1; y++)
                {
                    for (int x = 1; x < selectedReference.Dose.XSize - 1; x++)
                    {
                        // Store reference dose value (it will be used multiple times)
                        refval = rscale * (double)rarray[x, y] + roffset;

                        // If this reference dose is below threshold, exclude from statistics
                        if (refval < threshold)
                            continue;

                        // Calculate absolute dose difference
                        diff = Math.Abs(tscale * (double)t1array[x, y] + toffset - refval);

                        // Keep track of maximum statistics
                        if (diff > maxDiff)
                            maxDiff = diff;
                        if (refval > maxRef)
                            maxRef = refval;

                        // If the difference is less than the Gamma absolute criterion, this voxel passes and no DTA
                        // searching is needed. Note, this assumes (a) the dose volumes are perfectly alinged and (b) a
                        // local gamma is being calculated.
                        if (diff / refval <= gammaFrac)
                            gammaPass++;

                        // If not, search around by calculating Gamma at midpoint to each adjacent voxel. The following 
                        // else if statements calculate the interpolated dose a half voxel away in each of the three
                        // dimensions and check if the gamma function is 1 or less. This approach is a fairly low
                        // resolution version of more robust gamma tools but is necessary given the limited processing 
                        // power of the Citrix environment. See the GitHub repository for details on validation.
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x - 1, y]) / 2
                                + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2) 
                                + Math.Pow(0.5 * selectedReference.Dose.XRes / gammaDTA, 2)) <= 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x + 1, y]) / 2
                                + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2) 
                                + Math.Pow(0.5 * selectedReference.Dose.XRes / gammaDTA, 2)) <= 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x, y - 1]) / 2
                                + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2) 
                                + Math.Pow(0.5 * selectedReference.Dose.YRes / gammaDTA, 2)) <= 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x, y + 1]) / 2
                                + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2) 
                                + Math.Pow(0.5 * selectedReference.Dose.ZRes / gammaDTA, 2)) <= 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t0array[x, y]) / 2
                                + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2)
                                + Math.Pow(0.5 * selectedReference.Dose.ZRes / gammaDTA, 2)) <= 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t2array[x, y]) / 2
                                + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2)
                                + Math.Pow(0.5 * selectedReference.Dose.ZRes / gammaDTA, 2)) <= 1)
                            gammaPass++;
                        else
                            gammaFail++;
                    }
                }
            }

            // To expedite calculation, this tool does not calculate the actual gamma minima for each voxel but only 
            // tallies whether it passes or fails. The pass rate is the number of voxels greater than the threshold
            // that passed divided by the total voxels (pass + fail) that were also greater than the threshold. The 
            // result is stored back on the UI as a percentage, rounded to one decimal.
            double gammaRate = Math.Round((double)gammaPass / ((double)gammaPass + (double)gammaFail) * 1000) / 10;
            uiPassRate.Text = gammaRate.ToString() + "%";

            // The maximum absolute difference is also tracked during comparison is displayed to the UI as both an
            // absolute dose and percentage of the maximum reference dose.
            double percentDiff = Math.Round(maxDiff / maxRef * 1000) / 10;
            maxDiff = Math.Round(maxDiff * 10) / 10;
            uiMaxDiff.Text = maxDiff.ToString() + " Gy (" + percentDiff + "%)";


            // Display a message box to the user indicating that calculations have completed
            MessageBox.Show("Calcuations completed");
        }
    }
}
