using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Strangler Fig Self-Extracting Installer")]
[assembly: AssemblyProduct("Strangler Fig Migration Installer")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace StranglerSfx
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "StranglerSetup_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string zipFile = Path.Combine(tempDir, "payload.zip");

                using (Stream resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                {
                    if (resStream == null)
                    {
                        MessageBox.Show("Installer payload resource not found.", "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 1;
                    }
                    using (FileStream fs = File.Create(zipFile))
                    {
                        resStream.CopyTo(fs);
                    }
                }

                ZipFile.ExtractToDirectory(zipFile, tempDir);
                File.Delete(zipFile);

                string entryScript = File.Exists(Path.Combine(tempDir, "Install-Client.ps1"))
                    ? "Install-Client.ps1"
                    : "Install-Server.ps1";

                string scriptPath = Path.Combine(tempDir, entryScript);
                string passedArgs = string.Join(" ", args);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" " + passedArgs,
                    WorkingDirectory = tempDir,
                    UseShellExecute = false
                };

                using (Process proc = Process.Start(psi))
                {
                    proc.WaitForExit();
                    return proc.ExitCode;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Setup initialization failed:\n" + ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
