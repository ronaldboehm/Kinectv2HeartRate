using RDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KinectHeartRateResearch
{
    /// <summary>
    /// Encapsulates the JADE Independent Component Analysis (ICA) algorithm. 
    /// 
    /// Requires R (https://www.r-project.org/) with the JADE package 
    /// (https://cran.r-project.org/web/packages/JADE/index.html, documentation at
    ///  https://cran.r-project.org/web/packages/JADE/JADE.pdf).
    /// </summary>
    public class JadeIcaFacade : IDisposable
    {
        private REngine engine;
        private bool initialized;

        private CsvFile csvFile;

        public JadeIcaFacade()
        {
            engine = REngine.GetInstance();
            engine.Initialize();

            csvFile = new CsvFile();

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

        public double ProcessData(bool keepFile)
        {
            if (!initialized)
                throw new InvalidOperationException("R was not initialized properly. Cannot process data.");

            csvFile.EndWrite();

            var currentDir = System.Environment.CurrentDirectory.Replace('\\', '/');
#if DEBUG_TEST
                engine.Evaluate(string.Format("heartRateData <- read.csv('{0}/NormHeartRate_r61.csv')", currentDir.Replace('\\', '/')));
#else

            engine.Evaluate(string.Format("heartRateData <- read.csv('{0}', sep=',', dec='.')", csvFile.Path.Replace('\\', '/')));
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
                System.IO.File.Delete(csvFile.Path);
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

        internal void Begin()
        {
            csvFile.BeginWrite();
        }

        internal void Write(long elapsedMilliseconds, double norm_alpha, double norm_red, double norm_green, double norm_blue, double norm_ir)
        {
            csvFile.Write(elapsedMilliseconds,
                          norm_alpha, norm_red, norm_green, norm_blue, norm_ir);
        }

        private class CsvFile
        {
            private string m_filePath;
            private System.IO.FileStream m_fileStream;

            internal CsvFile()
            {
            }

            internal string Path { get { return m_filePath; } }

            internal void BeginWrite()
            {
                m_filePath = string.Format("{0}\\NormHeartRate_{1}{2}{3}{4}{5}{6}.{7}.csv", System.Environment.CurrentDirectory, DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second, DateTime.Now.Millisecond);

                m_fileStream = new System.IO.FileStream(m_filePath, System.IO.FileMode.CreateNew, System.IO.FileAccess.ReadWrite, System.IO.FileShare.ReadWrite, 512, true);

                //string header = "nAlpha,nRed,nGreen,nBlue,nIr\n";
                string header = "nMillisecondsElapsed,nBlue,nGreen,nRed,nAlpha,nIr\n";
                var headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
                m_fileStream.Write(headerBytes, 0, headerBytes.Length);
            }

            internal void Write(long elapsedMilliseconds, double norm_alpha, double norm_red, double norm_green, double norm_blue, double norm_ir)
            {
                if (m_fileStream == null)
                    return; // threading problem - can still attempt to write after file stream was closed -> just ignore the call then
                    // throw new InvalidOperationException("Cannot write to file - file stream was not created.");

                string data = string.Format(
                    "{0},{1},{2},{3},{4},{5}\n",
                    elapsedMilliseconds.ToString(EnNumberFormat),
                    norm_alpha.ToString(         EnNumberFormat),
                    norm_red.ToString(           EnNumberFormat),
                    norm_green.ToString(         EnNumberFormat),
                    norm_blue.ToString(          EnNumberFormat),
                    norm_ir.ToString(            EnNumberFormat)
                    );

                var bytesToWrite = System.Text.Encoding.UTF8.GetBytes(data);

                m_fileStream.WriteAsync(bytesToWrite, 0, bytesToWrite.Length);
            }

            private static System.Globalization.CultureInfo EnNumberFormat = new System.Globalization.CultureInfo("en-US");

            internal void EndWrite()
            {
                if (null != m_fileStream)
                {
                    m_fileStream.Flush();
                    m_fileStream.Close();
                    m_fileStream = null;
                }
            }
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
