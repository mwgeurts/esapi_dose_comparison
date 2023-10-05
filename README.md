# ESAPI Dose Comparison plugin

by Mark Geurts <mark.w.geurts@gmail.com>
<br>Copyright &copy; 2023, Aspirus Health

## Description

`DoseComparison.easpi.dll` is a standalone ESAPI plugin that allows users to quantify the difference between two dose calculations of the same plan using a local gamma evaluation. This can be useful when performing Treatment Planning System (TPS) QA following a software update or beam model change. This type of evaluation is recommended as part of the period TPS QA in AAPM MPPG 5: 

Geurts MW, Jacqmin DJ, Jones LE, Kry SF, Mihailidis DN, Ohrt JD, Ritter T, Smilowitz JB, Wingreen NE. [AAPM MEDICAL PHYSICS PRACTICE GUIDELINE 5.b: Commissioning and QA of treatment planning dose calculations-Megavoltage photon and electron beams](https://doi.org/10.1002/acm2.13641). J Appl Clin Med Phys. 2022 Sep;23(9):e13641. doi: 10.1002/acm2.13641. Epub 2022 Aug 10. PMID: 35950259; PMCID: PMC9512346.

## Installation

To install this plugin, download a release and copy the .dll into the `PublishedScripts` folder of the Varian file server, then if required, register the script under Script Approvals in Eclipse. Alternatively, download the code from this repository and compile it yourself using Visual Studio.

## Usage and Documentation

1. Open a non-clinical patient course in External Beam Planning
2. Select Tools > Scripts, then choose DoseComparison.easpi.dll from the list
3. On the UI window that appears, choose two plans to compare to using the drop down menus. The plans must be from the same course and must have identical dose grids (size and origin).
4. Edit the gamma criteria if desired and click Compare Plans. Note that DTA is currently fixed to the largest dose grid size dimension.
5. The calculation takes several minutes depending on dose grid size and magnitude of disagreement. Refill your water bottle while you wait.
6. Once finished the local gamma pass rate and maximum dose difference between the two volumes will be reported. 

## License

Released under the GNU GPL v3.0 License for evaluating and testing purposes only. This tool should NOT be used to evaluate clinical plans or make decisions that impact patient care. See the [LICENSE](LICENSE) file for further details.
