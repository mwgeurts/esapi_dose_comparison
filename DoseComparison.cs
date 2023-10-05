using System.Linq;
using System;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using System.Windows;

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

    /// <summary>
    /// CalculateGamma is the main computation function of this script. It calculates the gamma pass rate between two dose volumes
    /// </summary>
    /// <param name="reference">reference VMS.TPS.Common.Model.API.Dose object</param>
    /// <param name="target">target VMS.TPS.Common.Model.API.Dose object</param>
    /// <param name="percent">absolute local gamma criterion, expressed as a percentage</param>
    /// <param name="dta">distance to agreement gamma criterion</param>
    /// <param name="threshold">absolute dose threshold below which the dose volume will be excluded from evaluation</param>
    /// <returns>3-element array: gamma pass rate, max absolute dose difference, and difference relative to the maximum dose</returns>
    public static double[] CalculateGamma(Dose reference, Dose target, double percent, double dta, double threshold)
    {
        // Initialize return variables
        double[] returnVariables = new double[3];

        // Define the calculation resolution. Gamma will be calculated in each direction this many times between each voxel.
        double r = 32;

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
        double refval;
        double diff;
        double v0;
        bool flag;

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

                    // If the difference is less than the gamma absolute criterion, this voxel passes and no DTA
                    // searching is needed. Note, this assumes (a) the dose volumes are perfectly alinged and (b) a
                    // local gamma is being calculated.
                    if (diff / refval * 100 <= percent)
                    {
                        gammaPass++;
                        continue;
                    }

                    // Reset pass flag to false (assume gamma criteria not met)
                    flag = false;

                    // Create a loop that walks outward based on the resolution. At each step, gamma is calculated along
                    // each axis (-X, +X, -Y, +Y, -Z, +Z). Note, this loop is broken as soon as one passing value is found
                    // to speed up calculation.
                    for (double i = 0; i < r; i++)
                    {
                        // The target dose is linearly interpolated at each step. v0 stores the weighted initial value as it
                        // is re-used in each if statement below.
                        v0 = (1 - i / r) * (tscale * (double)t1array[x, y] + toffset);

                        // Evaluate gamma along -X axis
                        if (Math.Sqrt(Math.Pow(v0 + i / r * (tscale * (double)t1array[x - 1, y] + toffset) - refval, 2) 
                            / Math.Pow(refval * percent / 100, 2) + Math.Pow(i / r * reference.XRes / dta, 2)) < 1)
                        {
                            flag = true;
                            break;
                        }

                        // Evaluate gamma along +X axis
                        else if (Math.Sqrt(Math.Pow(v0 + i / r * (tscale * (double)t1array[x + 1, y] + toffset) - refval, 2) 
                            / Math.Pow(refval * percent / 100, 2) + Math.Pow(i / r * reference.XRes / dta, 2)) < 1)
                        {
                            flag = true;
                            break;
                        }

                        // Evaluate gamma along -Y axis
                        else if (Math.Sqrt(Math.Pow(v0 + i / r * (tscale * (double)t1array[x, y - 1] + toffset) - refval, 2) 
                            / Math.Pow(refval * percent / 100, 2) + Math.Pow(i / r * reference.YRes / dta, 2)) < 1)
                        {
                            flag = true;
                            break;
                        }

                        // Evaluate gamma along +Y axis
                        else if (Math.Sqrt(Math.Pow(v0 + i / r * (tscale * (double)t1array[x, y + 1] + toffset) - refval, 2) 
                            / Math.Pow(refval * percent / 100, 2) + Math.Pow(i / r * reference.YRes / dta, 2)) < 1)
                        {
                            flag = true;
                            break;
                        }

                        // Evaluate gamma along -Z axis
                        else if (Math.Sqrt(Math.Pow(v0 + i / r * (tscale * (double)t0array[x, y] + toffset) - refval, 2) 
                            / Math.Pow(refval * percent / 100, 2) + Math.Pow(i / r * reference.ZRes / dta, 2)) < 1)
                        {
                            flag = true;
                            break;
                        }

                        // Evaluate gamma along +Z axis
                        else if (Math.Sqrt(Math.Pow(v0 + i / r * (tscale * (double)t2array[x, y] + toffset) - refval, 2) 
                            / Math.Pow(refval * percent / 100, 2) + Math.Pow(i / r * reference.ZRes / dta, 2)) < 1)
                        {
                            flag = true;
                            break;
                        }
                    }

                    // If one of the above if statements passed, the flag will be true, so increment the number of passing
                    // voxels. If the flag is still false, no passing values were found, so increment the number of failing.
                    if (flag)
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

        // The maximum absolute difference is also tracked during comparison and passed back to the UI as both an
        // absolute dose and percentage of the maximum reference dose.
        returnVariables[1] = Math.Round(maxDiff * 10) / 10;
        returnVariables[2] = Math.Round(maxDiff / maxRef * 1000) / 10;

        // Join the return variables into an array
        return returnVariables;

    }
  }
}
