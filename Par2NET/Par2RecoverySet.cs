﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Par2NET.Packets;
using System.IO;

namespace Par2NET
{
    public class Par2RecoverySet
    {
        public CreatorPacket CreatorPacket = null;
        public MainPacket MainPacket = null;
        public List<RecoveryPacket> RecoveryPackets = new List<RecoveryPacket>();
        public Dictionary<string, FileVerification> FileSets = new Dictionary<string, FileVerification>();
        public List<FileVerification> SourceFiles = new List<FileVerification>();

        public uint completefilecount = 0;       // How many files are fully verified
        public uint renamedfilecount = 0;        // How many files are verified but have the wrong name
        public uint damagedfilecount = 0;        // How many files exist but are damaged
        public uint missingfilecount = 0;        // How many files are completely missing

        public ulong chunksize = 0;              // How much of a block can be processed.
        public uint sourceblockcount = 0;        // The total number of blocks
        public uint availableblockcount = 0;     // How many undamaged blocks have been found
        public uint missingblockcount = 0;       // How many blocks are missing

        public ulong totaldata = 0;              // Total amount of data to be processed.

        public byte[] inputbuffer = null;             // Buffer for reading DataBlocks (chunksize)
        public byte[] outputbuffer = null;            // Buffer for writing DataBlocks (chunksize * missingblockcount)

        private List<FileVerification> verifylist = new List<FileVerification>();

        private FileVerification FileVerification(string fileid)
        {
            if (!FileSets.Keys.Contains(fileid))
                FileSets.Add(fileid, new FileVerification());

            return FileSets[fileid];
        }

        public bool CheckPacketsConsistency()
        {
            if (MainPacket == null)
                return false;

            // Remove bad recovery packets
            foreach (RecoveryPacket badRecoveryPacket in (from packet in RecoveryPackets
                                                          where (packet.header.length - (ulong)packet.GetSize()) != MainPacket.blocksize
                                                          select packet))
            {
                RecoveryPackets.Remove(badRecoveryPacket);
            }

            ulong block_size = MainPacket.blocksize;
            List<string> keysToBeRemoved = new List<string>();

            foreach (string key in FileSets.Keys)
            {
                FileVerification fileVer = FileSets[key];

                if (fileVer.FileDescriptionPacket == null)
                    keysToBeRemoved.Add(key);

                if (fileVer.FileVerificationPacket == null)
                    continue;

                ulong file_size = fileVer.FileDescriptionPacket.length;
                ulong block_count = fileVer.FileVerificationPacket.blockcount;

                if ((file_size + block_size - 1) / block_size != block_count)
                    keysToBeRemoved.Add(key);
            }

            return true;
        }

        internal void AddCreatorPacket(CreatorPacket createPacket)
        {
            if (CreatorPacket == null)
                CreatorPacket = createPacket;
        }

        internal void AddDescriptionPacket(FileDescriptionPacket descPacket)
        {
            string fileid = ToolKit.ToHex(descPacket.fileid);

            if (FileVerification(fileid).FileDescriptionPacket == null)
                FileVerification(fileid).FileDescriptionPacket = descPacket;
        }

        internal void AddMainPacket(MainPacket mainPacket)
        {
            if (MainPacket == null)
                MainPacket = mainPacket;
        }

        internal void AddRecoveryPacket(RecoveryPacket recoveryPacket)
        {
            RecoveryPackets.Add(recoveryPacket);
        }

        internal void AddVerificationPacket(FileVerificationPacket verPacket)
        {
            string fileid = ToolKit.ToHex(verPacket.fileid);

            if (FileVerification(fileid).FileVerificationPacket == null)
                FileVerification(fileid).FileVerificationPacket = verPacket;
        }

        internal bool CreateSourceFileList()
        {
            foreach (byte[] fileidBytes in MainPacket.fileids)
            {
                string fileid = ToolKit.ToHex(fileidBytes);

                if (!this.FileSets.Keys.Contains(fileid))
                    continue;

                FileVerification fileVer = FileSets[fileid];
                fileVer.TargetFileName = Par2Library.ComputeTargetFileName(fileVer.FileDescriptionPacket.name);

                if (!SourceFiles.Contains(fileVer))
                    SourceFiles.Add(fileVer);
            }

            return true;
        }

        internal bool AllocateSourceBlocks()
        {
            ulong sourceblockcount = 0;

            foreach (FileVerification fileVer in SourceFiles)
            {
                sourceblockcount += fileVer.FileVerificationPacket.blockcount;
            }

            // Why return true if there is no sourceblock available ?
            if (sourceblockcount <= 0)
                return true;

            ulong totalsize = 0;

            foreach (FileVerification fileVer in SourceFiles)
            {
                totalsize += fileVer.FileDescriptionPacket.length;
                ulong blockcount = fileVer.FileVerificationPacket.blockcount;
                fileVer.SourceBlocks = new List<DataBlock>();
                fileVer.TargetBlocks = new List<DataBlock>();

                if (blockcount > 0)
                {
                    ulong filesize = fileVer.FileDescriptionPacket.length;

                    for (ulong i = 0; i < blockcount; i++)
                    {
                        DataBlock dataBlock = new DataBlock();
                        dataBlock.offset = i * MainPacket.blocksize;
                        dataBlock.length = Math.Min(MainPacket.blocksize, filesize - (i * MainPacket.blocksize));
                        fileVer.SourceBlocks.Add(dataBlock);
                        fileVer.TargetBlocks.Add(new DataBlock());
                    }
                }
            }

            return true;
        }

        internal bool VerifySourceFiles()
        {
            try
            {
                bool result = true;

                foreach (FileVerification fileVer in SourceFiles)
                {
                    if (!File.Exists(fileVer.TargetFileName))
                        continue;

                    result &= (VerifyFile(fileVer) != null);
                        
                }   

                return result;
            }
            catch
            {
                return false;
            }
        }

        internal bool QuickVerifySourceFiles()
        {
            try
            {
                bool result = true;

                foreach (FileVerification fileVer in SourceFiles)
                {
                    if (!File.Exists(fileVer.TargetFileName))
                        continue;

                    result &= (QuickVerifyFile(fileVer) != null);

                }

                return result;
            }
            catch
            {
                return false;
            }
        }

        private bool? QuickVerifyFile(Par2NET.FileVerification fileVer)
        {
            long filesize = 0;
            uint nb_blocks = 0;
            byte[] md5hash = null;
            byte[] md5hash16k = null;

            FileChecker.QuickCheckFile(fileVer.TargetFileName, (int)this.MainPacket.blocksize, out filesize, out nb_blocks, out md5hash16k, out md5hash);

            return false;
        }

        private bool VerifyDataFile(FileVerification fileVer)
        {
            return (bool)VerifyFile(fileVer);
        }

        private bool? VerifyFile(Par2NET.FileVerification fileVer)
        {
            FileChecker.CheckFile(fileVer.TargetFileName, (int)this.MainPacket.blocksize, fileVer.FileVerificationPacket.entries, fileVer.FileDescriptionPacket.hash16k, fileVer.FileDescriptionPacket.hashfull);

            return false;
        }

        // Rename any damaged or missnamed target files.
        public bool RenameTargetFiles()
        {
            // Rename any damaged target files
            foreach (FileVerification fileVer in SourceFiles)
            {
                // If the target file exists but is not a complete version of the file
                if (fileVer.GetTargetExists() && fileVer.GetTargetFile() != fileVer.GetCompleteFile())
                {
                    if (!fileVer.GetTargetFile().Rename())
                        return false;

                    // We no longer have a target file
                    fileVer.SetTargetExists(false);
                    fileVer.SetTargetFile(null);
                }
            }

            // Rename any missnamed but complete versions of the files
            foreach (FileVerification fileVer in SourceFiles)
            {
                // If there is no targetfile and there is a complete version
                if (fileVer.GetTargetFile() == null && fileVer.GetCompleteFile() != null)
                {
                    if (!fileVer.GetCompleteFile().Rename(fileVer.TargetFileName))
                        return false;

                    // This file is now the target file
                    fileVer.SetTargetExists(true);
                    fileVer.SetTargetFile(fileVer.GetCompleteFile());

                    // We have one more complete file
                    completefilecount++;
                }
            }

            return true;
        }

        public bool CreateTargetFiles()
        {
            uint filenumber = 0;

            // Create any missing target files
            foreach (FileVerification fileVer in SourceFiles)
            {

                // If the file does not exist
                if (!fileVer.GetTargetExists())
                {
                    DiskFile targetfile = new DiskFile();
                    string filename = fileVer.TargetFileName;
                    ulong filesize = fileVer.FileDescriptionPacket.length;

                    // Create the target file
                    if (!targetfile.Create(filename, filesize))
                    {
                        return false;
                    }

                    // This file is now the target file
                    fileVer.SetTargetExists(true);
                    fileVer.SetTargetFile(targetfile);

                    ulong offset = 0;

                    // Allocate all of the target data blocks
                    for (int index = 0; index < fileVer.TargetBlocks.Count; index++)
                    {
                        DataBlock tb = fileVer.TargetBlocks[index];
                        tb.offset = offset;
                        tb.length = Math.Min(MainPacket.blocksize, filesize - offset);

                        offset += MainPacket.blocksize;
                    }

                    // Add the file to the list of those that will need to be verified
                    // once the repair has completed.
                    verifylist.Add(fileVer);
                }
            }

            return true;
        }

        internal bool ComputeRSmatrix()
        {
            throw new NotImplementedException();
        }

        internal void DeleteIncompleteTargetFiles()
        {
            foreach (FileVerification fileVer in verifylist)
            {
                if (fileVer.GetTargetExists())
                {
                    DiskFile targetFile = fileVer.GetTargetFile();

                    if (targetFile.IsOpen())
                        targetFile.Close();

                    targetFile.Delete();

                    fileVer.SetTargetExists(false);
                    fileVer.SetTargetFile(null);
                }
            }
        }

        // Verify that all of the reconstructed target files are now correct.
        // Do this in multiple threads if appropriate (1 thread per processor).
        internal bool VerifyTargetFiles()
        {
            bool finalresult = true;

            // Verify the target files in alphabetical order
            verifylist.Sort();

	        foreach( FileVerification fileVer in verifylist)
            {
                DiskFile targetfile = fileVer.GetTargetFile();

                // Close the file
                if (targetfile.IsOpen())
                    targetfile.Close();

                // Mark all data blocks for the file as unknown
                foreach (DataBlock db in fileVer.SourceBlocks)
                {
                    db.ClearLocation();
                }

                // Say we don't have a complete version of the file
                fileVer.SetCompleteFile(null);

                // Re-open the target file
                if (!targetfile.Open())
                {
                    finalresult &= false;
                    continue;
                }

                // Verify the file again
                //if (!VerifyDataFile(targetfile, fileVer))
                if (!VerifyDataFile(fileVer))
                    finalresult &= false;

                // Close the file again
                targetfile.Close();

                // Find out how much data we have found
                UpdateVerificationResults();
            }

            return finalresult;
        }

        // Find out how much data we have found
        private void UpdateVerificationResults()
        {
            availableblockcount = 0;
            missingblockcount = 0;

            completefilecount = 0;
            renamedfilecount = 0;
            damagedfilecount = 0;
            missingfilecount = 0;

            foreach (FileVerification sourcefile in SourceFiles)
            {
                // Was a perfect match for the file found
                if (sourcefile.GetCompleteFile() != null)
                {
                    // Is it the target file or a different one
                    if (sourcefile.GetCompleteFile() == sourcefile.GetTargetFile())
                    {
                        completefilecount++;
                    }
                    else
                    {
                        renamedfilecount++;
                    }

                    availableblockcount += (uint)sourcefile.FileVerificationPacket.blockcount;
                }
                else
                {
                    // Count the number of blocks that have been found
                    foreach (DataBlock sb in sourcefile.SourceBlocks)
                    {
                        if (sb.IsSet())
                            availableblockcount++;
                    }

                    // Does the target file exist
                    if (sourcefile.GetTargetExists())
                    {
                        damagedfilecount++;
                    }
                    else
                    {
                        missingfilecount++;
                    }
                }
            }

            missingblockcount = sourceblockcount - availableblockcount;
        }

        // Allocate memory buffers for reading and writing data to disk.
        internal bool AllocateBuffers(ulong memoryLimit)
        {
            // Would single pass processing use too much memory
            if (MainPacket.blocksize * missingblockcount > memoryLimit)
            {
                // Pick a size that is small enough
                chunksize = (ulong) (~3 & (int)(memoryLimit / missingblockcount));
            }
            else
            {
                chunksize = MainPacket.blocksize;
            }

            try
            {
                // Allocate the two buffers
                inputbuffer = new byte[chunksize];
                outputbuffer = new byte[chunksize * missingblockcount];
            }
            catch (OutOfMemoryException oome)
            {
                //cerr << "Could not allocate buffer memory." << endl;
                return false;
            }

            return true;
        }

        internal bool ProcessData(ulong blockoffset, uint blocklength)
        {
            throw new NotImplementedException();
        }
    }
}
