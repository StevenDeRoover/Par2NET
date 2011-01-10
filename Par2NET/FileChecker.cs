﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using Par2NET.Packets;
using System.Diagnostics;

namespace Par2NET
{
    public class FileChecker
    {
        private static byte[] MD5Hash16k(string filename)
        {
            using (BinaryReader br = new BinaryReader(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 16384)))
            {
                return MD5.Create().ComputeHash(br.ReadBytes(16384));
            }
        }

        private static byte[] MD5Hash(string filename)
        {
            using (BinaryReader br = new BinaryReader(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                return MD5.Create().ComputeHash(br.BaseStream);
            }
        }

        public static bool QuickCheckFile(string filename, int blocksize, out long filesize, out uint nb_blocks, out byte[] md5hash16k, out byte[] md5hash)
        {
            filesize = 0;
            nb_blocks = 0;
            md5hash = null;
            md5hash16k = null;

            try
            {
                FileInfo fiFile = new FileInfo(filename);

                if (!fiFile.Exists)
                    return false;

                filesize = fiFile.Length;
                nb_blocks = blocksize > 0 ? (filesize % blocksize == 0 ? (uint)(filesize / blocksize) : (uint)(filesize / blocksize + 1)) : 0;
                md5hash = MD5Hash(filename);
                md5hash16k = MD5Hash16k(filename);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);

                return false;
            }
        }

        private static object _syncObject = new object();

        private static object _readerSyncObject = new object();

        public static void CheckFile(DiskFile diskFile, string filename, int blocksize, List<FileVerificationEntry> fileVerEntry, byte[] md5hash16k, byte[] md5hash, ref MatchType matchType, Dictionary<uint,FileVerificationEntry> hashfull, Dictionary<uint,FileVerificationEntry> hash, List<FileVerificationEntry> expectedList, ref int expectedIndex)
        {
            //TODO : Maybe rewrite in TPL for slide calculation
            //TODO : Maybe search against all sourcefiles (case of misnamed files)

            matchType = MatchType.FullMatch;

            ulong _offset = 0;
            bool stop = false;

            Console.WriteLine("Checking file '{0}'", Path.GetFileName(filename));

            //using (BinaryReader br = new BinaryReader(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 2*blocksize)))
            //using (BinaryReader br = new BinaryReader(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 10*1024*1024, FileOptions.SequentialScan)))
            using (BinaryReader br = new BinaryReader(new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                CRC32NET.CRC32 crc32Hasher = new CRC32NET.CRC32();
                MD5 md5Hasher = MD5.Create();
                FastCRC32.FastCRC32 crc32 = new FastCRC32.FastCRC32((ulong)blocksize);

                uint partial_key = (uint)(Path.GetFileName(filename).GetHashCode());

                long length = br.BaseStream.Length;
                Stream baseStream = br.BaseStream;

                if (length < blocksize)
                    blocksize = (int)length;

                while (baseStream.Position < length)
                {
                    int nbRead = Math.Min((2 * blocksize), (int)(length - baseStream.Position));

                    byte[] buffer = null;
                    //lock (_readerSyncObject)
                    //{
                        // Prepare buffer
                        buffer = br.ReadBytes(nbRead);
                    //}
                    int offset = 0;

                    bool stepping = false;

                    byte inch = 0;
                    byte outch = 0;

                    uint crc32Value = 0;

                    do
                    {
                        // Compute crc32 for current slice

                        if (!stepping)
                        {
                            crc32Value = crc32.CRCUpdateBlock(0xFFFFFFFF, (uint)blocksize, buffer, (uint)offset) ^ 0xFFFFFFFF;
                        }
                        else
                        {
                            inch = buffer[offset + blocksize - 1];

                            crc32Value = crc32.windowMask ^ crc32.CRCSlideChar(crc32.windowMask ^ crc32Value, inch, outch);
                        }

                        stepping = false;

                        FileVerificationEntry entry = null;

                        lock (_syncObject)
                        {
                            entry = expectedList[expectedIndex > expectedList.Count-1 ? expectedList.Count - 1 : expectedIndex];
                        }

                        if (entry.crc != crc32Value)
                        {
                            entry = null;

                            uint key = crc32Value ^ partial_key;

                            if (hashfull.ContainsKey(key))
                            {
                                entry = hashfull[key];
                            }
                            else
                            {
                                if (hash.ContainsKey(crc32Value))
                                    entry = hash[crc32Value];
                            }
                        }

                        if (entry != null)
                        {
                            byte[] blockhash = md5Hasher.ComputeHash(buffer, offset, blocksize);

                            if (ToolKit.ToHex(blockhash) == ToolKit.ToHex(entry.hash))
                            {
                                // We found a complete match, so go to next block !

                                //Console.WriteLine("block found at offset {0}, crc {1}", _offset, entry.crc);

                                entry.SetBlock(diskFile, (int)(_offset));

                                offset += blocksize;
                                _offset += (ulong)blocksize;

                                lock (_syncObject)
                                {
                                    expectedIndex = expectedList.IndexOf(entry) + 1;
                                }
                            }
                        }
                        else
                        {
                            if (baseStream.Position == length && (int)_offset > length)
                            {
                                int i = 8;
                                int j = 2 * i;
                                //break;
                            }
                            else
                            {
                                matchType = MatchType.PartialMatch;
                                outch = buffer[offset];
                                ++offset;
                                ++_offset;
                                stepping = true;
                            }
                        }

                        if (offset >= 2 * blocksize)
                            break;

                        if (offset >= blocksize)
                        {
                            if (baseStream.Position == length)
                            {
                                int i = 8;
                                int j = 2 * i;
                                //break;
                            }

                            byte[] newBuffer = new byte[buffer.Length];
                            Buffer.BlockCopy(buffer, offset, newBuffer, 0, 2 * blocksize - offset);

                            byte[] readBytes = null;

                            //lock (_readerSyncObject)
                            //{
                                readBytes = br.ReadBytes(Math.Min(offset, (int)(length - baseStream.Position)));
                            //}

                            Buffer.BlockCopy(readBytes, 0, newBuffer, 2 * blocksize - offset, readBytes.Length);

                            offset = 0;
                            
                            buffer = newBuffer;

                            if (stop)
                                break;

                            if (readBytes.Length == 0)
                                stop = true;
                        }

                    } while (offset < 2*blocksize); // Stop condition : When index is equal to blocksize, end of sliding buffer is reached, so we have to read from file
                }
            }

            bool atLeastOne = false;
            foreach (FileVerificationEntry entry in fileVerEntry)
            {
                if (entry.datablock.diskfile != null)
                {
                    atLeastOne = true;
                    break;
                }
            }

            if (!atLeastOne)
                matchType = MatchType.NoMatch;
        }
    }
}

