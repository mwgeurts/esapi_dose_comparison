using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace DoseComparison
{

    /// <summary>
    /// Interaction logic for PlanMenu.xaml
    /// </summary>
    public partial class PlanMenu : UserControl
    {

        public ScriptContext context;

        public PlanMenu()
        {
            InitializeComponent();
        }

        public void FillPlansComboBoxes(Course c)
        {
            foreach (PlanSetup p in c.PlanSetups)
            {
                uiReferenceList.Items.Add(p);
                uiTargetList.Items.Add(p);
            }
        }

        private void SetDTA(object sender, SelectionChangedEventArgs e)
        {
            foreach (PlanSetup p in context.Course.PlanSetups)
            {
                if (p.Id == uiReferenceList.SelectedValue.ToString())
                {
                    uiDTA.Text = p.Dose.XRes.ToString() + " mm";
                }
            }
            uiPassRate.Text = "";
            uiMaxDiff.Text = "";
        }

        private void ClearResults(object sender, SelectionChangedEventArgs e)
        {
            uiPassRate.Text = "";
            uiMaxDiff.Text = "";
        }

        private void ValidateThreshold(object sender, KeyboardFocusChangedEventArgs e)
        {
            Double.TryParse(Regex.Match(uiThreshold.Text, @"\d+\.*\d*").Value, out double test);
            if (test > 0)
            {
                test = Math.Round(test * 10) / 10;
                uiThreshold.Text = test.ToString() + " Gy";
            }
            else
                uiThreshold.Text = "0.1 Gy";

            uiPassRate.Text = "";
            uiMaxDiff.Text = "";
        }

        private void ValidatePercent(object sender, KeyboardFocusChangedEventArgs e)
        {
            Double.TryParse(Regex.Match(uiPercent.Text, @"\d+\.*\d*").Value, out double test);
            if (test > 0)
            {
                test = Math.Round(test * 10) / 10;
                uiPercent.Text = test.ToString() + "%";
            }
            else
                uiPercent.Text = "1.0%";

            uiPassRate.Text = "";
            uiMaxDiff.Text = "";

        }

        private void ComparePlans(object sender, RoutedEventArgs e)
        {
            PlanSetup selectedTarget = null;
            PlanSetup selectedReference = null;

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

            selectedTarget.DoseValuePresentation = DoseValuePresentation.Absolute;
            selectedReference.DoseValuePresentation = DoseValuePresentation.Absolute;

            // Verify dose dimensions are the same
            if (selectedTarget.Dose.XSize != selectedReference.Dose.XSize ||
                selectedTarget.Dose.YSize != selectedReference.Dose.YSize ||
                selectedTarget.Dose.ZSize != selectedReference.Dose.ZSize)
            {
                MessageBox.Show("ERROR: the two dose volumes must be the same.");
                return;
            }

            double maxDiff = 0;
            double maxRef = 0;
            int gammaPass = 0;
            int gammaFail = 0;
            double gammaFrac = Double.Parse(Regex.Match(uiPercent.Text, @"\d+\.*\d*").Value) / 100;
            double gammaDTA = selectedReference.Dose.XRes;
            double threshold = Double.Parse(Regex.Match(uiThreshold.Text, @"\d+\.*\d*").Value);

            double refval = 0;
            double diff = 0;
            double tscale = selectedTarget.Dose.VoxelToDoseValue(1).Dose - selectedTarget.Dose.VoxelToDoseValue(0).Dose;
            double toffset = selectedTarget.Dose.VoxelToDoseValue(0).Dose / tscale;
            double rscale = selectedReference.Dose.VoxelToDoseValue(1).Dose - selectedReference.Dose.VoxelToDoseValue(0).Dose;
            double roffset = selectedReference.Dose.VoxelToDoseValue(0).Dose / rscale;

            int[,] t0array = new int[selectedTarget.Dose.XSize, selectedTarget.Dose.YSize];
            int[,] t1array = new int[selectedTarget.Dose.XSize, selectedTarget.Dose.YSize];
            int[,] t2array = new int[selectedTarget.Dose.XSize, selectedTarget.Dose.YSize];
            int[,] rarray = new int[selectedReference.Dose.XSize, selectedReference.Dose.YSize];

            // Note, for loops exclude the edge values, as each neighboring voxel is included in the DTA search
            for (int z = 1; z < selectedReference.Dose.ZSize - 1; z++)
            {

                selectedReference.Dose.GetVoxels(z, rarray);


                // If the entire 
                if (rscale * rarray.Cast<int>().Max() + roffset < threshold)
                    continue;

                selectedTarget.Dose.GetVoxels(z - 1, t0array);
                selectedTarget.Dose.GetVoxels(z, t1array);
                selectedTarget.Dose.GetVoxels(z + 1, t2array);

                for (int y = 1; y < selectedReference.Dose.YSize - 1; y++)
                {
                    for (int x = 1; x < selectedReference.Dose.XSize - 1; x++)
                    {
                        // Store reference dose value (it will be used multiple times)
                        refval = rscale * (double)rarray[x, y] + roffset;

                        // If this dose is below threshold, exclude from statistics
                        if (refval < threshold)
                            continue;

                        // Calculate absolute dose difference
                        diff = Math.Abs(tscale * (double)t1array[x, y] + toffset - refval);

                        // Keep track of maximum statistics
                        if (diff > maxDiff)
                            maxDiff = diff;
                        if (refval > maxRef)
                            maxRef = refval;

                        // Check is voxel passes absolute difference
                        if (diff / refval <= gammaFrac)
                            gammaPass++;

                        // If not, search around by calculating Gamma at midpoint to each adjacent voxel
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x - 1, y]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2) + 0.25) < 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x + 1, y]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2) + 0.25) < 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x, y - 1]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2) + 0.25) < 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x, y + 1]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2) + 0.25) < 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t0array[x, y]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2)
                            + Math.Pow(0.5 * selectedReference.Dose.ZRes / gammaDTA, 2)) < 1)
                            gammaPass++;
                        else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t2array[x, y]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * gammaFrac, 2)
                            + Math.Pow(0.5 * selectedReference.Dose.ZRes / gammaDTA, 2)) < 1)
                            gammaPass++;
                        else
                            gammaFail++;
                    }
                }
            }

            double gammaRate = Math.Round((double)gammaPass / ((double)gammaPass + (double)gammaFail) * 1000) / 10;
            uiPassRate.Text = gammaRate.ToString() + "%";

            double percentDiff = Math.Round(maxDiff / maxRef * 1000) / 10;
            maxDiff = Math.Round(maxDiff * 10) / 10;
            uiMaxDiff.Text = maxDiff.ToString() + " Gy (" + percentDiff + "%)";

            MessageBox.Show("Calcuations completed");
        }
    }
}
