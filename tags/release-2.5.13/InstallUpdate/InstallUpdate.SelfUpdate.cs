﻿using System;
using System.Diagnostics;
using System.IO;
using wyUpdate.Common;
using wyUpdate.Compression.Vcdiff;

namespace wyUpdate
{
    partial class InstallUpdate
    {
        //for self update
        public string NewSelfLoc;
        public string OldSelfLoc;

        public void RunSelfUpdate()
        {
            Exception except = null;

            try
            {
                //extract downloaded self update
                ExtractUpdateFile();

                try
                {
                    // remove update file (it's no longer needed)
                    File.Delete(Filename);
                }
                catch { }


                //find and forcibly close oldClientLocation
                KillProcess(OldSelfLoc);

                string updtDetailsFilename = Path.Combine(OutputDirectory, "updtdetails.udt");

                if (File.Exists(updtDetailsFilename))
                {
                    UpdtDetails = UpdateDetails.Load(updtDetailsFilename);

                    //remove the file to prevent conflicts with the regular product update
                    File.Delete(updtDetailsFilename);
                }


                // generate files from patches
                CreatewyUpdateFromPatch();


                //find self in Path.Combine(OutputDirectory, "base")
                UpdateFile updateFile = FindNewClient();


                //transfer new client to the directory (Note: this assumes a standalone wyUpdate - i.e. no dependencies)
                File.Copy(NewSelfLoc, OldSelfLoc, true);

                //Optimize client if necessary
                if (updateFile != null)
                    NGenInstall(OldSelfLoc, updateFile.CPUVersion);

                //cleanup the client update files to prevent conflicts with the product update
                File.Delete(NewSelfLoc);
                Directory.Delete(Path.Combine(OutputDirectory, "base"));
            }
            catch (Exception ex)
            {
                except = ex;
            }

            if (canceled || except != null)
            {
                //report cancellation
                ThreadHelper.ReportProgress(Sender, SenderDelegate, "Cancelling update...", -1, -1);

                //Delete temporary files
                if (except != null && except.GetType() != typeof(PatchApplicationException))
                {
                    // remove the entire temp directory
                    try
                    {
                        Directory.Delete(OutputDirectory, true);
                    }
                    catch { }
                }
                else
                {
                    //only 'gut' the folder leaving the server file

                    string[] dirs = Directory.GetDirectories(TempDirectory);

                    foreach (string dir in dirs)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch { }
                    }
                }

                ThreadHelper.ReportError(Sender, SenderDelegate, string.Empty, except);
            }
            else
            {
                ThreadHelper.ReportSuccess(Sender, SenderDelegate, "Self update complete");
            }
        }

        public void JustExtractSelfUpdate()
        {
            Exception except = null;

            try
            {
                if (!Directory.Exists(OutputDirectory))
                    Directory.CreateDirectory(OutputDirectory);

                //extract downloaded self update
                ExtractUpdateFile();

                try
                {
                    // remove update file (it's no longer needed)
                    File.Delete(Filename);
                }
                catch { }


                string updtDetailsFilename = Path.Combine(OutputDirectory, "updtdetails.udt");

                if (File.Exists(updtDetailsFilename))
                {
                    UpdtDetails = UpdateDetails.Load(updtDetailsFilename);
                }


                // generate files from patches
                CreatewyUpdateFromPatch();


                //find self in Path.Combine(OutputDirectory, "base")
                FindNewClient();
            }
            catch (Exception ex)
            {
                except = ex;
            }

            if (canceled || except != null)
            {
                //report cancellation
                ThreadHelper.ReportProgress(Sender, SenderDelegate, "Cancelling update...", -1, -1);

                //Delete temporary files
                if (except != null)
                {
                    // remove the entire temp directory
                    try
                    {
                        Directory.Delete(OutputDirectory, true);
                    }
                    catch { }
                }

                ThreadHelper.ReportError(Sender, SenderDelegate, string.Empty, except);
            }
            else
            {
                ThreadHelper.ReportSuccess(Sender, SenderDelegate, "Self update extraction complete");
            }
        }

        public void JustInstallSelfUpdate()
        {
            Exception except = null;

            try
            {
                string updtDetailsFilename = Path.Combine(OutputDirectory, "updtdetails.udt");

                if (File.Exists(updtDetailsFilename))
                {
                    UpdtDetails = UpdateDetails.Load(updtDetailsFilename);
                }

                //find self in Path.Combine(OutputDirectory, "base")
                UpdateFile updateFile = FindNewClient();

                //find and forcibly close oldClientLocation
                KillProcess(OldSelfLoc);

                //transfer new client to the directory (Note: this assumes a standalone client - i.e. no dependencies)
                File.Copy(NewSelfLoc, OldSelfLoc, true);

                //Optimize client if necessary
                if (updateFile != null)
                    NGenInstall(OldSelfLoc, updateFile.CPUVersion);
            }
            catch (Exception ex)
            {
                except = ex;
            }


            if (canceled || except != null)
            {
                //report cancellation
                ThreadHelper.ReportProgress(Sender, SenderDelegate, "Cancelling update...", -1, -1);

                //Delete temporary files
                if (except != null)
                {
                    // remove the entire temp directory
                    try
                    {
                        Directory.Delete(OutputDirectory, true);
                    }
                    catch { }
                }

                ThreadHelper.ReportError(Sender, SenderDelegate, string.Empty, except);
            }
            else
            {
                ThreadHelper.ReportSuccess(Sender, SenderDelegate, "Self update complete");
            }
        }


        void CreatewyUpdateFromPatch()
        {
            // generate files from patches

            if (Directory.Exists(Path.Combine(OutputDirectory, "patches")))
            {
                // set the base directory to the home of the client file
                ProgramDirectory = Path.GetDirectoryName(OldSelfLoc);
                TempDirectory = OutputDirectory;

                // patch the file (assume only one - wyUpdate.exe)

                if (UpdtDetails.UpdateFiles[0].DeltaPatchRelativePath != null)
                {
                    string tempFilename = Path.Combine(TempDirectory, UpdtDetails.UpdateFiles[0].RelativePath);

                    // create the directory to store the patched file
                    if (!Directory.Exists(Path.GetDirectoryName(tempFilename)))
                        Directory.CreateDirectory(Path.GetDirectoryName(tempFilename));

                    try
                    {
                        using (FileStream original = File.OpenRead(OldSelfLoc))
                        using (FileStream patch = File.OpenRead(Path.Combine(TempDirectory, UpdtDetails.UpdateFiles[0].DeltaPatchRelativePath)))
                        using (FileStream target = File.Open(tempFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                        {
                            VcdiffDecoder.Decode(original, patch, target, UpdtDetails.UpdateFiles[0].NewFileAdler32);
                        }
                    }
                    catch
                    {
                        throw new PatchApplicationException("Patch failed to apply to " + FixUpdateDetailsPaths(UpdtDetails.UpdateFiles[0].RelativePath));
                    }

                    // the 'last write time' of the patch file is really the 'lwt' of the dest. file
                    File.SetLastWriteTime(tempFilename, File.GetLastWriteTime(Path.Combine(TempDirectory, UpdtDetails.UpdateFiles[0].DeltaPatchRelativePath)));
                }


                try
                {
                    // remove the patches directory (frees up a bit of space)
                    Directory.Delete(Path.Combine(TempDirectory, "patches"), true);
                }
                catch { }
            }
        }

        UpdateFile FindNewClient()
        {
            //first search the update details file
            for (int i = 0; i < UpdtDetails.UpdateFiles.Count; i++)
            {
                if (UpdtDetails.UpdateFiles[i].IsNETAssembly)
                {
                    //optimize (ngen) the file
                    NewSelfLoc = Path.Combine(OutputDirectory, UpdtDetails.UpdateFiles[i].RelativePath);

                    return UpdtDetails.UpdateFiles[i];
                }
            }

            //not found yet, so keep searching
            //get a list of files in the "base" folder
            string[] files = Directory.GetFiles(Path.Combine(OutputDirectory, "base"), "*.exe", SearchOption.AllDirectories);

            if (files.Length > 0)
            {
                NewSelfLoc = files[0];
            }
            else
            {
                throw new Exception("New wyUpdate couldn't be found.");
            }

            //not ngen-able
            return null;
        }

        static void KillProcess(string filename)
        {
            Process[] aProcess = Process.GetProcesses();

            foreach (Process proc in aProcess)
            {
                //The Try{} block needs to be outside the if statement because 'proc.MainModule'
                // can throw an exception in more than one case (x64 processes for x86 wyUpdate, 
                // permissions for Vista / 7, etc.) wyUpdate will be detected despite the try/catch block.
                try
                {
                    if (proc.MainModule.FileName.ToLower() == filename.ToLower())
                    {
                        proc.Kill();
                    }
                }
                catch { }
            }
        }
    }
}