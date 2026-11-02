using RdbExporter.Utilities;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace RdbExporter.Parsers
{
    public class SwfImageParser
    {
        static readonly SwfTagCode[] IMAGE_CODES = { SwfTagCode.DefineBitsLossless, SwfTagCode.DefineBitsLossless2, SwfTagCode.DefineBitsJpeg2, SwfTagCode.DefineBitsJpeg3, SwfTagCode.DefineBitsJpeg4 };

        public static IEnumerable<SKBitmap> ParseImagesFromSwfFile(string file)
        {
            return ParseImagesFromSwfFile(File.OpenRead(file));
        }

        public static IEnumerable<SKBitmap> ParseImagesFromSwfFile(Stream stream)
        {
            using (stream)
            {
                using var reader = new BittableBinaryReader(stream);

                var header = Encoding.ASCII.GetString(reader.ReadBytes(3));
                if (header == "ZWS") throw new InvalidOperationException($"Unspported file type '{header}'.");
                else if (header != "CWS" && header != "FWS")
                {
                    throw new InvalidDataException("Not a valid SWF file.");
                }

                var versionNumber = reader.ReadByte();
                var fileLength = reader.ReadInt32(); //This is the uncompressed length, so won't match file size for CWF

                if (header == "CWS")
                {
                    //CWS Files are compressed with Zlib
                    reader.ReadBytes(2); //Skip the zlib header, since DeflateStream won't process it correctly
                    using var compressedReader = new BittableBinaryReader(new DeflateStream(reader.BaseStream, CompressionMode.Decompress));
                    return ReadHeaderAndImages(compressedReader);
                }
                return ReadHeaderAndImages(reader);
            }
        }
        private static IEnumerable<SKBitmap> ReadHeaderAndImages(BittableBinaryReader reader)
        {
            ReadRect(reader);
            var framerate = reader.ReadInt16();
            var frameCount = reader.ReadInt16();

            var tags = ReadTags(reader);

            return tags.Where(t => IMAGE_CODES.Contains(t.Code)).Select(t => HandleImageTag(t));
        }

        private static void ReadRect(BittableBinaryReader reader)
        {
            //This is the "twip" sized boundaries of the SWF
            int length = reader.ReadBits(5); //Bit length of bounds
            _ = reader.ReadBits(length); //xMin
            _ = reader.ReadBits(length); //xMax
            _ = reader.ReadBits(length); //yMin
            _ = reader.ReadBits(length); //yMax
        }

        private static List<SwfTag> ReadTags(BittableBinaryReader reader)
        {
            var tags = new List<SwfTag>();
            do
            {
                tags.Add(ReadTag(reader));
            } while (tags[tags.Count - 1].Code != SwfTagCode.End);
            return tags;
        }

        private static SwfTag ReadTag(BittableBinaryReader reader)
        {
            uint tagCodeAndLength = reader.ReadUInt16();
            uint code = (tagCodeAndLength & 0b1111_1111_1100_0000) >> 6; //upper 10 bits are code
            uint length = tagCodeAndLength & 0b0011_1111; //lower 6 bits are length
            if (length == 0b0011_1111)
            {
                //If all length bits are set, this is a large block and uses a UInt to signal size
                length = reader.ReadUInt32();
            }
            var data = reader.ReadBytes((int)length); //This cast isn't safe, probably should check this
            return new SwfTag() { Code = (SwfTagCode)code, Data = data };
        }

        private static SKBitmap HandleImageTag(SwfTag tag)
        {
            return tag.Code switch
            {
                SwfTagCode.DefineBitsLossless => HandleLossless(tag, false),
                SwfTagCode.DefineBitsLossless2 => HandleLossless(tag, true),
                SwfTagCode.DefineBitsJpeg3 => HandleJpeg3(tag),
                SwfTagCode.DefineBitsJpeg2 => HandleJpeg2(tag),
                _ => throw new Exception($"Didn't handle code type {tag.Code}"),
            };
        }

        private static SKBitmap HandleLossless(SwfTag tag, bool alpha)
        {
            var binaryReader = new BinaryReader(new MemoryStream(tag.Data));
            var characterId = binaryReader.ReadUInt16();
            var format = binaryReader.ReadByte();
            var width = binaryReader.ReadUInt16();
            var height = binaryReader.ReadUInt16();
            int[] colorTable = null;
            if (format == 3)
            {
                colorTable = new int[binaryReader.ReadByte() + 1]; //Color table size
            }
            if (format != 5 && format != 3) throw new Exception($"Unsupported Lossless format '{format}'");

            binaryReader.ReadBytes(2); //Eat the Zlib header
            var decompressedData = ReadAll(new DeflateStream(binaryReader.BaseStream, CompressionMode.Decompress));

            int dataIndex = 0;
            if(format == 3)
            {
                for(int i = 0; i < colorTable.Length; i++)
                {
                    if (alpha)
                    {
                        //32 bit RGBA
                        colorTable[i] = Rgba(decompressedData[dataIndex + 0], decompressedData[dataIndex + 1], decompressedData[dataIndex + 2], decompressedData[dataIndex + 3]);
                        dataIndex += 4; 
                    }
                    else
                    {
                        //24 bit RGB
                        colorTable[i] = Rgba(decompressedData[dataIndex + 0], decompressedData[dataIndex + 1], decompressedData[dataIndex + 2], 255);
                        dataIndex += 3;
                    }
                }
            }

            var pixels = new int[width * height];
            int imageIndex = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (format == 5)
                    {
                        if (alpha)
                        {
                            //32 bit ARGB
                            pixels[imageIndex] = Rgba(decompressedData[dataIndex + 1], decompressedData[dataIndex + 2], decompressedData[dataIndex + 3], decompressedData[dataIndex]);
                            dataIndex += 4;
                        }
                        else
                        {
                            //32 bits, but the first 8 bits should be ignored, rest is RGB
                            pixels[imageIndex] = Rgba(decompressedData[dataIndex + 1], decompressedData[dataIndex + 2], decompressedData[dataIndex + 3], 255);
                            dataIndex += 4;
                        }
                    }
                    else if (format == 3)
                    {
                        //8 bit, pointer to color table
                        pixels[imageIndex] = colorTable[decompressedData[dataIndex]];
                        dataIndex += 1;
                    }
                    imageIndex += 1;
                }

                //Scan lines are aligned to 32-bit lengths, check if we need to adjust for padding
                if (format == 3 && width % 4 > 0)
                {
                    dataIndex += 4 - width % 4;
                }
            }
            //Lossless2 colors are stored premultiplied by alpha
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            return bitmap;
        }

        private static int Rgba(byte r, byte g, byte b, byte a)
        {
            return r | g << 8 | b << 16 | a << 24;
        }

        private static SKBitmap HandleJpeg3(SwfTag tag)
        {
            using var binaryReader = new BinaryReader(new MemoryStream(tag.Data, 0, 6));
            _ = binaryReader.ReadUInt16(); //characterId
            int alphaDataOffset = (int)binaryReader.ReadUInt32(); //Not a safe cast probably
            return DecodeImage(FixJpegTables(tag.Data, 6, alphaDataOffset));
        }

        private static SKBitmap HandleJpeg2(SwfTag tag)
        {
            using var binaryReader = new BinaryReader(new MemoryStream(tag.Data, 0, 2));
            _ = binaryReader.ReadUInt16(); //characterId
            return DecodeImage(FixJpegTables(tag.Data, 2, tag.Data.Length - 2));
        }

        private static SKBitmap DecodeImage(Stream stream)
        {
            return SKBitmap.Decode(stream) ?? throw new ArgumentException("Unable to decode image data.");
        }

        //SWF JPEG data can have the encoding tables in their own SOI..EOI block, directly followed by the image's SOI
        //Remove the EOI/SOI pair between them so it becomes a single valid JPEG
        private static MemoryStream FixJpegTables(byte[] data, int offset, int length)
        {
            int end = offset + length;
            int i = offset + 2; //Skip SOI
            while (i + 4 <= end && data[i] == 0xFF)
            {
                byte marker = data[i + 1];
                if (marker == 0xD9)
                {
                    if (data[i + 2] == 0xFF && data[i + 3] == 0xD8)
                    {
                        var merged = new byte[length - 4];
                        Array.Copy(data, offset, merged, 0, i - offset);
                        Array.Copy(data, i + 4, merged, i - offset, end - i - 4);
                        return new MemoryStream(merged);
                    }
                    break;
                }
                if (marker == 0xDA) break; //Start of scan, no separate tables block
                i += 2 + (data[i + 2] << 8 | data[i + 3]);
            }
            return new MemoryStream(data, offset, length);
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (stream)
            using (var memoryStream = new MemoryStream(1))
            {
                stream.CopyTo(memoryStream);
                return memoryStream.GetBuffer();
            }
        }
    }

    public class SwfTag
    {
        public SwfTagCode Code { get; set; }
        public byte[] Data { get; set; }
    }

    public enum SwfTagCode : uint
    {
        /// <summary>
        /// End tag, always the last tag in the file
        /// </summary>
        End = 0,
        ShowFrame = 1,
        DefineShape = 2,
        PlaceObject = 4,
        RemoveObject = 5,
        DefineBits = 6,
        JpegTables = 8,
        SetBackgroundColor = 9,
        DefineText = 11,
        DoAction = 12,
        DefineBitsLossless = 20,
        DefineBitsJpeg2 = 21,
        DefineShape2 = 22,
        Protect = 24,
        PlaceObject2 = 26,
        RemoveObject2 = 28,
        DefineShape3 = 32,
        DefineBitsJpeg3 = 35,
        DefineBitsLossless2 = 36,
        DefineEditText = 37,
        FrameLabel = 43,
        /// <summary>
        /// This will always be first tag in SWF 8 and later
        /// </summary>
        FileAttributes = 69,
        ImportAssets = 71,
        DefineFontAlignZones = 73,
        CsmTextSettings = 74,
        DefineFont3 = 75,
        SymbolClass = 76,
        Metadata = 77,
        DefineScalingGrid = 78,
        DefineSceneAndFrameLabelData = 86,
        DefineFontName = 88,
        DefineBitsJpeg4 = 90
    }

}