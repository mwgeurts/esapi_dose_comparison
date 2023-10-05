using System.Linq;
using System;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;

namespace VMS.TPS
{
  public class Script
  {
    public Script()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]

    // Execute() is called by Eclipse when this script is run
    public void Execute(ScriptContext context, System.Windows.Window window/*, ScriptEnvironment environment*/)
    {
        // Launch a new user interface defined by PlanMenu.xaml
        var userInterface = new DoseComparison.PlanMenu();
        window.Title = "Choose plans to compare";
        window.Content = userInterface;
        window.Width = 500;
        window.Height = 350;

        // Populate the reference and target drop down menus with a list of the current patient course's plans
        userInterface.FillPlansComboBoxes(context.Course);

        // Pass the current patient context to the UI
        userInterface.context = context;
    }

    // CalculateGamma is the main computation function of this script. It takes in dose volumes and Gamma criteria and returns
    // an array of Gamma pass rate (as a percentage), maximum dose difference, and max difference as a percentage of the max
    // reference dose.
    public static double[] CalculateGamma(Dose reference, Dose target, double frac, double dta, double threshold)
    {
        // Initialize return variables
        double[] returnVariables = new double[3];

        // Initialize the results statistics that will be used/displayed at the end of this computation
        double maxDiff = 0;
        double maxRef = 0;
        int gammaPass = 0;
        int gammaFail = 0;

        // Calculate the scale and offset values (stored in the first two dose values). This is needed because the GetVoxels()
        // method employed below returns integer dose values, which must be converted back to dose by the scale/offset. It is 
        // computationally faster to store these and apply the scaling/offset manually rather than call VoxelToDoseValue() on 
        // every single voxel in both plans.
        double tscale = target.VoxelToDoseValue(1).Dose - target.VoxelToDoseValue(0).Dose;
        double toffset = target.VoxelToDoseValue(0).Dose / tscale;
        double rscale = reference.VoxelToDoseValue(1).Dose - reference.VoxelToDoseValue(0).Dose;
        double roffset = reference.VoxelToDoseValue(0).Dose / rscale;

        // Initialize temporary variables that will be used during the for loops
        double refval = 0;
        double diff = 0;

        // Initialize 2D arrays that will be reused during the for loops. Note there are three 2D planes that will be retrieved
        // From the target dose volume each iteration; this is to allow the gamma calculation to be 3-dimensional. This is also 
        // why DTA must be restricted.
        int[,] rarray = new int[reference.XSize, reference.YSize];
        int[,] t0array = new int[target.XSize, target.YSize];
        int[,] t1array = new int[target.XSize, target.YSize];
        int[,] t2array = new int[target.XSize, target.YSize];

        // Loop through each planar dose in the Z dimension. Note, these for loops exclude the edges (starting at 1, ending at
        // size-1), as each neighboring plane is included in the DTA search. This assumes that the edge voxels are all below 
        // The threshold dose, otherwise the accuracy may be impacted. That said, it would be relatively straightforward to add 
        // the additional bounds checks to the if statements below. 
        for (int z = 1; z < reference.ZSize - 1; z++)
        {
            // Retrieve the current reference dose plane index
            reference.GetVoxels(z, rarray);

            // If the entire dose plane is less than the dose threshold, skip ahead now and save some computation time
            if (rscale * rarray.Cast<int>().Max() + roffset < threshold)
                continue;

            // Retrieve the previous, current, and next adjacent target dose planes
            target.GetVoxels(z - 1, t0array);
            target.GetVoxels(z, t1array);
            target.GetVoxels(z + 1, t2array);

            // Loop through each voxel of the 2D plane. See above note about excluding the edge values.
            for (int y = 1; y < reference.YSize - 1; y++)
            {
                for (int x = 1; x < reference.XSize - 1; x++)
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
                    if (diff / refval <= frac)
                        gammaPass++;

                    // If not, search around by calculating Gamma at midpoint to each adjacent voxel. The following 
                    // else if statements calculate the interpolated dose a half voxel away in each of the three
                    // dimensions and check if the gamma function is 1 or less. This approach is a fairly low
                    // resolution version of more robust gamma tools but is necessary given the limited processing 
                    // power of the Citrix environment. See the GitHub repository for details on validation.
                    else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x - 1, y]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * frac, 2)
                            + Math.Pow(0.5 * reference.XRes / dta, 2)) <= 1)
                        gammaPass++;
                    else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x + 1, y]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * frac, 2)
                            + Math.Pow(0.5 * reference.XRes / dta, 2)) <= 1)
                        gammaPass++;
                    else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x, y - 1]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * frac, 2)
                            + Math.Pow(0.5 * reference.YRes / dta, 2)) <= 1)
                        gammaPass++;
                    else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t1array[x, y + 1]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * frac, 2)
                            + Math.Pow(0.5 * reference.ZRes / dta, 2)) <= 1)
                        gammaPass++;
                    else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t0array[x, y]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * frac, 2)
                            + Math.Pow(0.5 * reference.ZRes / dta, 2)) <= 1)
                        gammaPass++;
                    else if (Math.Sqrt(Math.Pow(tscale * ((double)t1array[x, y] + (double)t2array[x, y]) / 2
                            + toffset - refval, 2) / Math.Pow(refval * frac, 2)
                            + Math.Pow(0.5 * reference.ZRes / dta, 2)) <= 1)
                        gammaPass++;
                    else
                        gammaFail++;
                }
            }
        }


        // To expedite calculation, this tool does not calculate the actual gamma minima for each voxel but only 
        // tallies whether it passes or fails. The pass rate is the number of voxels greater than the threshold
        // that passed divided by the total voxels (pass + fail) that were also greater than the threshold. The 
        // result is passed back to the UI as a percentage, rounded to one decimal.
        returnVariables[0] = Math.Round((double)gammaPass / ((double)gammaPass + (double)gammaFail) * 1000) / 10;

        // The maximum absolute difference is also tracked during comparison and returned to the UI as both an
        // absolute dose and percentage of the maximum reference dose.
        returnVariables[1] = Math.Round(maxDiff * 10) / 10;
        returnVariables[2] = Math.Round(maxDiff / maxRef * 1000) / 10;

        // Join the return variables into an array
        return returnVariables;

    }
  }
}
