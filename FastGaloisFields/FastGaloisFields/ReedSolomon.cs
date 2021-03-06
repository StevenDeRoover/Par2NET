﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FastGaloisFieldsUnsafe;

namespace FastGaloisFields
{
    public class ReedSolomon
    {
        private static IFastGaloisFieldsProcessor processor = null;

        static ReedSolomon()
        {
            processor = FastGaloisFieldsFactory.GetProcessor();
        }

        protected static uint gcd(uint a, uint b)
        {
            if (a != 0 && b != 0)
            {
                while (a != 0 && b != 0)
                {
                    if (a > b)
                    {
                        a = a % b;
                    }
                    else
                    {
                        b = b % a;
                    }
                }

                return a + b;
            }
            else
            {
                return 0;
            }
        }

        // Use Gaussian Elimination to solve the matrices
        protected static bool GaussElim(uint rows, uint leftcols, ushort[] leftmatrix, ushort[] rightmatrix, uint datamissing)
        {
            //if (noiselevel == CommandLine::nlDebug)
            //{
            //  for (unsigned int row=0; row<rows; row++)
            //  {
            //    cout << ((row==0) ? "/"    : (row==rows-1) ? "\\"    : "|");
            //    for (unsigned int col=0; col<leftcols; col++)
            //    {
            //      cout << " "
            //           << hex << setw(G::Bits>8?4:2) << setfill('0')
            //           << (unsigned int)leftmatrix[row*leftcols+col];
            //    }
            //    cout << ((row==0) ? " \\ /" : (row==rows-1) ? " / \\" : " | |");
            //    for (unsigned int col=0; col<rows; col++)
            //    {
            //      cout << " "
            //           << hex << setw(G::Bits>8?4:2) << setfill('0')
            //           << (unsigned int)rightmatrix[row*rows+col];
            //    }
            //    cout << ((row==0) ? " \\"   : (row==rows-1) ? " /"    : " | |");
            //    cout << endl;

            //    cout << dec << setw(0) << setfill(' ');
            //  }
            //}

            // Because the matrices being operated on are Vandermonde matrices
            // they are guaranteed not to be singular.

            // Additionally, because Galois arithmetic is being used, all calulations
            // involve exact values with no loss of precision. It is therefore
            // not necessary to carry out any row or column swapping.

            // Solve one row at a time

            //int progress = 0;

            // For each row in the matrix
            for (uint row = 0; row < datamissing; row++)
            {
                // NB Row and column swapping to find a non zero pivot value or to find the largest value
                // is not necessary due to the nature of the arithmetic and construction of the RS matrix.

                // Get the pivot value.
                ushort pivotvalue = rightmatrix[row * rows + row];

                if (pivotvalue == 0)
                {
                    //cerr << "RS computation error." << endl;
                    return false;
                }

                // If the pivot value is not 1, then the whole row has to be scaled
                if (pivotvalue != 1)
                {
                    for (uint col = 0; col < leftcols; col++)
                    {
                        if (leftmatrix[row * leftcols + col] != 0)
                        {
                            leftmatrix[row * leftcols + col] = processor.Divide(leftmatrix[row * leftcols + col], pivotvalue);
                        }
                    }
                    rightmatrix[row * rows + row] = 1;
                    for (uint col = row + 1; col < rows; col++)
                    {
                        if (rightmatrix[row * rows + col] != 0)
                        {
                            rightmatrix[row * rows + col] = processor.Divide(rightmatrix[row * rows + col], pivotvalue);
                        }
                    }
                }

                // For every other row in the matrix
                for (uint row2 = 0; row2 < rows; row2++)
                {
                    // Define MPDL to skip reporting and speed things up
                    //#ifndef MPDL
                    //      if (noiselevel > CommandLine::nlQuiet)
                    //      {
                    //        int newprogress = (row*rows+row2) * 1000 / (datamissing*rows);
                    //        if (progress != newprogress)
                    //        {
                    //          progress = newprogress;
                    //          cout << "Solving: " << progress/10 << '.' << progress%10 << "%\r" << flush;
                    //        }
                    //      }
                    //#endif

                    if (row != row2)
                    {
                        // Get the scaling factor for this row.
                        ushort scalevalue = rightmatrix[row2 * rows + row];

                        if (scalevalue == 1)
                        {
                            // If the scaling factor happens to be 1, just subtract rows
                            for (uint col = 0; col < leftcols; col++)
                            {
                                if (leftmatrix[row * leftcols + col] != 0)
                                {
                                    leftmatrix[row2 * leftcols + col] = processor.Minus(leftmatrix[row2 * leftcols + col], leftmatrix[row * leftcols + col]);
                                }
                            }

                            for (uint col = row; col < rows; col++)
                            {
                                if (rightmatrix[row * rows + col] != 0)
                                {
                                    rightmatrix[row2 * rows + col] = processor.Minus(rightmatrix[row2 * rows + col], rightmatrix[row * rows + col]);
                                }
                            }
                        }
                        else if (scalevalue != 0)
                        {
                            // If the scaling factor is not 0, then compute accordingly.
                            for (uint col = 0; col < leftcols; col++)
                            {
                                if (leftmatrix[row * leftcols + col] != 0)
                                {
                                    leftmatrix[row2 * leftcols + col] = processor.Multiply(processor.Minus(leftmatrix[row2 * leftcols + col], leftmatrix[row * leftcols + col]), scalevalue);
                                }
                            }

                            for (uint col = row; col < rows; col++)
                            {
                                if (rightmatrix[row * rows + col] != 0)
                                {
                                    rightmatrix[row2 * rows + col] = processor.Multiply(processor.Minus(rightmatrix[row2 * rows + col], rightmatrix[row * rows + col]), scalevalue);
                                }
                            }
                        }
                    }
                }
            }

            //if (noiselevel > CommandLine::nlQuiet)
            //  cout << "Solving: done." << endl;

            //if (noiselevel == CommandLine::nlDebug)
            //{
            //  for (unsigned int row=0; row<rows; row++)
            //  {
            //    cout << ((row==0) ? "/"    : (row==rows-1) ? "\\"    : "|");
            //    for (unsigned int col=0; col<leftcols; col++)
            //    {
            //      cout << " "
            //           << hex << setw(G::Bits>8?4:2) << setfill('0')
            //           << (unsigned int)leftmatrix[row*leftcols+col];
            //    }
            //    cout << ((row==0) ? " \\ /" : (row==rows-1) ? " / \\" : " | |");
            //    for (unsigned int col=0; col<rows; col++)
            //    {
            //      cout << " "
            //           << hex << setw(G::Bits>8?4:2) << setfill('0')
            //           << (unsigned int)rightmatrix[row*rows+col];
            //    }
            //    cout << ((row==0) ? " \\"   : (row==rows-1) ? " /"    : " | |");
            //    cout << endl;

            //    cout << dec << setw(0) << setfill(' ');
            //  }
            //}

            return true;
        }
    }
}
