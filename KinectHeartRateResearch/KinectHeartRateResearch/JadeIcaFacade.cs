using RDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KinectHeartRateResearch
{
    /// <summary>
    /// Encapsulates the JADE ICA algorithm. 
    /// 
    /// Requires R (https://www.r-project.org/) with the JADE package 
    /// (https://cran.r-project.org/web/packages/JADE/index.html, documentation at
    ///  https://cran.r-project.org/web/packages/JADE/JADE.pdf).
    /// </summary>
    public class JadeIcaFacade : IDisposable
    {
        private REngine engine;
        private bool initialized;

        public JadeIcaFacade()
        {
            engine = REngine.GetInstance();
            engine.Initialize();

            InitJadePackage();
        }

        private string WorkingDirectory { get { return System.Environment.CurrentDirectory; } }
        private string LibraryLocation  { get { return System.IO.Path.Combine(WorkingDirectory, "Libs"); } }

        private void InitJadePackage()
        {
            try
            {
                if (!System.IO.Directory.Exists(LibraryLocation))
                    System.IO.Directory.CreateDirectory(LibraryLocation);

                if (!System.IO.Directory.Exists(System.IO.Path.Combine(LibraryLocation, "JADE")))
                    engine.Evaluate("install.packages(\"JADE\", repos='http://cran.us.r-project.org', lib = \"{0}\")", LibraryLocation.Replace(@"\", @"\\"));

                // R also uses \ as the escape character, so replace \ with \\:
                engine.Evaluate("setwd(\"{0}\")", WorkingDirectory.Replace(@"\", @"\\"));
                engine.Evaluate("library(JADE, lib.loc=\"{0}\")", LibraryLocation.Replace(@"\", @"\\"));

                initialized = true;
            }

            catch (Exception e)
            {
                Console.WriteLine("Error initializing R." + e.ToString());
            }
        }

        public double ProcessData(string filePath, bool keepFile)
        {
            var currentDir = System.Environment.CurrentDirectory.Replace('\\', '/');
#if DEBUG_TEST
                engine.Evaluate(string.Format("heartRateData <- read.csv('{0}/NormHeartRate_r61.csv')", currentDir.Replace('\\', '/')));
#else

            engine.Evaluate(string.Format("heartRateData <- read.csv('{0}', sep=',', dec='.')", filePath.Replace('\\', '/')));
#endif
            engine.Evaluate(string.Format("source('{0}/RScripts/KinectHeartRate_JADE.r')", currentDir));

            //HR1 and HR4 are band filtered to match frequency of normal heart rate range
            //HR2 and HR3 are not and included incase your environment has closer matches to these frequencies which were seperated
            NumericVector hrVect1 = engine.GetSymbol("hr1").AsNumeric();
            NumericVector hrVect4 = engine.GetSymbol("hr4").AsNumeric();

            //In case your environment matches closer
            NumericVector hrVect2 = engine.GetSymbol("hr2").AsNumeric();
            NumericVector hrVect3 = engine.GetSymbol("hr3").AsNumeric();

            double hr1 = hrVect1.First();
            double hr4 = hrVect4.First();

            //incase you need these seperated frequencies
            double hr2 = hrVect2.First();
            double hr3 = hrVect3.First();

            double hr = (hr1 > hr4) ? hr1 : hr4;

            if (!keepFile)
            {
                System.IO.File.Delete(filePath);
            }

            return hr;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool isDisposing)
        {
            engine.Dispose();
            engine = null;
        }
    }

    public static class RExtensions
    {
        public static SymbolicExpression Evaluate(this REngine R, string format, object arg0)
        {
            return R.Evaluate(String.Format(format, arg0));
        }

        public static SymbolicExpression Evaluate(this REngine R, string format, params object[] args)
        {
            return R.Evaluate(String.Format(format, args));
        }

        public static IList<T> AsList<T>(this SymbolicExpression expression)
        {
            return expression.AsVector().ToArray().Select(obj => (T)obj).ToList();
        }
    }
}
