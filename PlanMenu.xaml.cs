using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Packaging;
using System.Linq;
using System.Text;
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
                reference.Items.Add(p);
                target.Items.Add(p);
            }
        }

        private void SetDTA(object sender, SelectionChangedEventArgs e)
        {
            foreach (PlanSetup p in context.Course.PlanSetups)
            {
                if (p.Id == reference.SelectedValue.ToString())
                {
                    dta.Text = (p.Dose.XRes * 10).ToString() + " mm";
                }
            }
        }

        private void ComparePlans(object sender, RoutedEventArgs e)
        {
            PlanSetup selectedTarget = null;
            PlanSetup selectedReference = null;

            foreach (PlanSetup p in context.Course.PlanSetups)
            {
                if (p.Id == target.SelectedValue.ToString())
                {
                    selectedTarget = p;
                }
                if (p.Id == reference.SelectedValue.ToString())
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

            double refval = 0;
            double diff = 0;
            double tscale = selectedTarget.Dose.VoxelToDoseValue(1).Dose - selectedTarget.Dose.VoxelToDoseValue(0).Dose;
            double toffset = selectedTarget.Dose.VoxelToDoseValue(0).Dose / tscale;
            double rscale = selectedReference.Dose.VoxelToDoseValue(1).Dose - selectedReference.Dose.VoxelToDoseValue(0).Dose;
            double roffset = selectedReference.Dose.VoxelToDoseValue(0).Dose / rscale;

            int[,] tarray = new int[selectedTarget.Dose.XSize, selectedTarget.Dose.YSize];
            int[,] rarray = new int[selectedReference.Dose.XSize, selectedReference.Dose.YSize];

            for (int z = 0; z < selectedTarget.Dose.ZSize; z++)
            {
                selectedTarget.Dose.GetVoxels(z, tarray);
                selectedReference.Dose.GetVoxels(z, rarray);

                for (int y = 0; y < selectedTarget.Dose.YSize; y++)
                {
                    for (int x = 0; x < selectedTarget.Dose.XSize; x++)
                    {
                        refval = rscale * (double)rarray[x,y] + roffset;
                        diff = Math.Abs(tscale * (double)tarray[x,y] + toffset - refval);
                        if (diff > maxDiff)
                            maxDiff = diff;
                        if (refval > maxRef)
                            maxRef = refval;
                    }
                }
            }

            double percentDiff = Math.Round(maxDiff / maxRef * 1000) / 10;
            MessageBox.Show("Maximum difference = " + maxDiff.ToString() + " Gy (" + percentDiff.ToString() + "%)\nMax Reference Dose = " + maxRef.ToString() + "Gy");
 
        }


    }
}
