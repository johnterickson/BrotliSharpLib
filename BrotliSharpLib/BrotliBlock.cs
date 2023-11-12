using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;

namespace BrotliSharpLib
{
    public static class BrotliBlock
    {

        public static readonly BitArray Window22 = new BitArray(new bool[] { true, true, false, true });
        public static readonly BitArray StartBlockBits;
        public static readonly byte[] StartBlockBytes;

        public static readonly BitArray EndBlock = new BitArray(new bool[] { true, true });
        public static readonly byte[] EndBlockBytes = new byte[] { 0x3 };

        static BrotliBlock()
        {
            StartBlockBits = new BitArray(Window22);
            PadMetaBlockToByteBoundary(StartBlockBits);
            StartBlockBytes = new byte[StartBlockBits.Length / 8];
            StartBlockBits.CopyTo(StartBlockBytes, 0);
        }


        public static bool TryExtractBareByteAlignedMetaBlock(byte[] buffer, out byte[] byteAlignedBareBlock)
        {
            // The contents of the compressed stream are sensitive to byte alignment -
            // it can only be "bit"-shifted in increments of 8.
            var bits = new BitArray(buffer);

            if (Enumerable.Range(0, StartBlockBits.Length).Any(i => StartBlockBits[i] != bits[i]))
            {
                byteAlignedBareBlock = null;
                return false;
            }

#if NETSTANDARD
            for (int i = StartBlockBits.Length; i < bits.Length; i++)
            {
                bits[i - StartBlockBits.Length] = bits[i];
                bits[i] = false;
            }
#else
            bits.RightShift(StartBlockBits.Length);
#endif
            bits.Length -= StartBlockBits.Length;

            // remove ISLAST, ISLASTEMPTY FROM END
            {
                var lastBitIndex = bits.Length - 1;
                while (bits[lastBitIndex] == false)
                {
                    lastBitIndex -= 1;
                }

                lastBitIndex -= 1;
                if (bits[lastBitIndex] == false)
                {
                    byteAlignedBareBlock = null;
                    return false;
                }

                bits.Length = lastBitIndex;
            }

            PadMetaBlockToByteBoundary(bits);
            byteAlignedBareBlock = new byte[bits.Length / 8];
            bits.CopyTo(byteAlignedBareBlock, 0);
            return true;
        }

        public static void PadMetaBlockToByteBoundary(BitArray bits)
        {
            if (bits.Length % 8 == 0)
            {
                return;
            }

            int blockLengthByteCount = (bits.Length + 6 + 7) / 8;
            int endOfBlock = bits.Length;
            bits.Length = blockLengthByteCount * 8;

            int bitIndex = endOfBlock;
            bits[bitIndex++] = false; // ISLAST
            bits[bitIndex++] = true; // MNIBBLES
            bits[bitIndex++] = true; // MNIBBLES
            bits[bitIndex++] = false; // reserved
            bits[bitIndex++] = false; // MSKIPBYTES
            bits[bitIndex++] = false; // MSKIPBYTES

            while (bitIndex < bits.Length)
            {
                bits[bitIndex++] = false;
            }
        }


        public static int HeaderBitLength(byte b)
        {
            /*
                  1..7 bits: WBITS, a value in the range 10..24, encoded with the
                             following variable-length code (as it appears in the
                             compressed data, where the bits are parsed from right
                             to left):

                                  Value    Bit Pattern
                                  -----    -----------
                                     10        0100001
                                     11        0110001
                                     12        1000001
                                     13        1010001
                                     14        1100001
                                     15        1110001
                                     16              0
                                     17        0000001
                                     18           0011
                                     19           0101
                                     20           0111
                                     21           1001
                                     22           1011
                                     23           1101
                                     24           1111
            */
            if ((b & 0x1) == 0)
            {
                return 1;
            }
            else
            {
                if ((b & 0x3) == 0x3)
                {
                    return 4;
                }
                else if ((b & 0xF) == 0x1 && (b & 0x7F) != 0x11)
                {
                    return 7;
                }
                else
                {
                    throw new ArgumentException($"Unexpected window byte: {b}");
                }
            }
        }
    }
}