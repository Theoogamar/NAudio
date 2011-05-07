using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace NAudio.Wave
{
    /// <summary>
    /// MPEG Layer flags
    /// </summary>
    public enum MpegLayer
    {
        /// <summary>
        /// Reserved
        /// </summary>
        Reserved,
        /// <summary>
        /// Layer 3
        /// </summary>
        Layer3,
        /// <summary>
        /// Layer 2
        /// </summary>
        Layer2,
        /// <summary>
        /// Layer 1
        /// </summary>
        Layer1
    }

    /// <summary>
    /// MPEG Version Flags
    /// </summary>
    public enum MpegVersion
    {
        /// <summary>
        /// Version 2.5
        /// </summary>
        Version25,
        /// <summary>
        /// Reserved
        /// </summary>
        Reserved,
        /// <summary>
        /// Version 2
        /// </summary>
        Version2,
        /// <summary>
        /// Version 1
        /// </summary>
        Version1
    }

    /// <summary>
    /// Channel Mode
    /// </summary>
    public enum ChannelMode
    {
        /// <summary>
        /// Stereo
        /// </summary>
        Stereo,
        /// <summary>
        /// Joint Stereo
        /// </summary>
        JointStereo,
        /// <summary>
        /// Dual Channel
        /// </summary>
        DualChannel,
        /// <summary>
        /// Mono
        /// </summary>
        Mono
    }

    /// <summary>
    /// Represents an MP3 Frame
    /// </summary>
    public class Mp3Frame
    {
        private static readonly int[,,] bitRates = new int[,,] {
            {
                // MPEG Version 1
                { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 }, // Layer 1
                { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 }, // Layer 2
                { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 }, // Layer 3
            },
            {
                // MPEG Version 2 & 2.5
                { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 }, // Layer 1
                { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }, // Layer 2 
                { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }, // Layer 3 (same as layer 2)
            }
        };

        private static readonly int[,] samplesPerFrame = new int[,] {
            {   // MPEG Version 1
                384,    // Layer1
                1152,   // Layer2
                1152    // Layer3
            },
            {   // MPEG Version 2 & 2.5
                384,    // Layer1
                1152,   // Layer2
                576     // Layer3
            }
        };
        
        private static readonly int[] sampleRatesVersion1 = new int[] { 44100, 48000, 32000 };
        private static readonly int[] sampleRatesVersion2 = new int[] { 22050, 24000, 16000 };
        private static readonly int[] sampleRatesVersion25 = new int[] { 11025, 12000, 8000 };

        private bool crcPresent;
        //private short crc;
        private int sampleRate;
        private int frameLengthInBytes;
        private ChannelMode channelMode;
        private const int MaxFrameLength = 16 * 1024;
        private int samplesInFrame; // number of samples in this frame

        /// <summary>
        /// Reads an MP3 frame from a stream
        /// </summary>
        /// <param name="input">input stream</param>
        /// <returns>A valid MP3 frame, or null if none found</returns>
        public static Mp3Frame LoadFromStream(Stream input)
        {
            return LoadFromStream(input, true);
        }

        /// <summary>Reads an MP3Frame from a stream</summary>
        /// <remarks>http://mpgedit.org/mpgedit/mpeg_format/mpeghdr.htm has some good info
        /// also see http://www.codeproject.com/KB/audio-video/mpegaudioinfo.aspx
        /// </remarks>
        /// <returns>A valid MP3 frame, or null if none found</returns>
        public static Mp3Frame LoadFromStream(Stream input, bool readData)
        {
            byte[] headerBytes = new byte[4];
            int bytesRead = input.Read(headerBytes, 0, headerBytes.Length);
            if (bytesRead < headerBytes.Length)
            {
                // reached end of stream, no more MP3 frames
                return null;
            }
            Mp3Frame frame = new Mp3Frame();

            while (!IsValidHeader(headerBytes, frame))
            {
                // shift down by one and try again
                headerBytes[0] = headerBytes[1];
                headerBytes[1] = headerBytes[2];
                headerBytes[2] = headerBytes[3];
                bytesRead = input.Read(headerBytes, 3, 1);
                if (bytesRead < 1)
                {
                    return null;
                }
            }
            /* no longer read the CRC since we include this in framelengthbytes
            if (this.crcPresent)
                this.crc = reader.ReadInt16();*/

            int bytesRequired = frame.frameLengthInBytes - 4;
            if (readData)
            {
                frame.RawData = new byte[frame.frameLengthInBytes];
                Array.Copy(headerBytes, frame.RawData, 4);
                bytesRead = input.Read(frame.RawData, 4, bytesRequired);
                if (bytesRead < bytesRequired)
                {
                    // TODO: could have an option to suppress this, although it does indicate a corrupt file
                    // for now, caller should handle this exception
                    throw new EndOfStreamException("Unexpected end of stream before frame complete");
                }
            }
            else
            {
                input.Position += bytesRequired;
            }

            return frame;
        }


        /// <summary>
        /// Constructs an MP3 frame
        /// </summary>
        private Mp3Frame()
        {

        }

        /// <summary>
        /// checks if the four bytes represent a valid header,
        /// if they are, will parse the values into Mp3Frame
        /// </summary>
        private static bool IsValidHeader(byte[] headerBytes, Mp3Frame frame)
        {
            if ((headerBytes[0] == 0xFF) && ((headerBytes[1] & 0xE0) == 0xE0))
            {
                // TODO: could do with a bitstream class here
                frame.MpegVersion = (MpegVersion)((headerBytes[1] & 0x18) >> 3);
                if (frame.MpegVersion == MpegVersion.Reserved)
                {
                    //throw new FormatException("Unsupported MPEG Version");
                    return false;
                }

                frame.MpegLayer = (MpegLayer)((headerBytes[1] & 0x06) >> 1);

                if (frame.MpegLayer == MpegLayer.Reserved)
                {
                    return false;
                }
                int layerIndex = frame.MpegLayer == MpegLayer.Layer1 ? 0 : frame.MpegLayer == MpegLayer.Layer2 ? 1 : 2;
                frame.crcPresent = (headerBytes[1] & 0x01) == 0x00;
                int bitRateIndex = (headerBytes[2] & 0xF0) >> 4;
                if (bitRateIndex == 15)
                {
                    // invalid index
                    return false;
                }
                int versionIndex = frame.MpegVersion == Wave.MpegVersion.Version1 ? 0 : 1;
                frame.BitRate = bitRates[versionIndex, layerIndex, bitRateIndex] * 1000;
                if (frame.BitRate == 0)
                {
                    return false;
                }
                int sampleFrequencyIndex = (headerBytes[2] & 0x0C) >> 2;
                if (sampleFrequencyIndex == 3)
                {
                    return false;
                }

                if (frame.MpegVersion == MpegVersion.Version1)
                {
                    frame.sampleRate = sampleRatesVersion1[sampleFrequencyIndex];
                }
                else if (frame.MpegVersion == MpegVersion.Version2)
                {
                    frame.sampleRate = sampleRatesVersion2[sampleFrequencyIndex];
                }
                else
                {
                    // mpegVersion == MpegVersion.Version25
                    frame.sampleRate = sampleRatesVersion25[sampleFrequencyIndex];
                }

                bool padding = (headerBytes[2] & 0x02) == 0x02;
                bool privateBit = (headerBytes[2] & 0x01) == 0x01;
                frame.channelMode = (ChannelMode)((headerBytes[3] & 0xC0) >> 6);
                int channelExtension = (headerBytes[3] & 0x30) >> 4;
                bool copyright = (headerBytes[3] & 0x08) == 0x08;
                bool original = (headerBytes[3] & 0x04) == 0x04;
                int emphasis = (headerBytes[3] & 0x03);

                int nPadding = padding ? 1 : 0;

                frame.samplesInFrame = samplesPerFrame[versionIndex, layerIndex];
                int coefficient = frame.samplesInFrame / 8;
                if (frame.MpegLayer == MpegLayer.Layer1)
                {
                    frame.frameLengthInBytes = (coefficient * frame.BitRate / frame.sampleRate + nPadding) * 4;
                }
                else
                {
                    frame.frameLengthInBytes = (coefficient * frame.BitRate) / frame.sampleRate + nPadding;
                }
                
                if (frame.frameLengthInBytes > MaxFrameLength)
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sample rate of this frame
        /// </summary>
        public int SampleRate
        {
            get { return sampleRate; }
        }

        /// <summary>
        /// Frame length in bytes
        /// </summary>
        public int FrameLength
        {
            get { return frameLengthInBytes; }
        }

        /// <summary>
        /// Bit Rate
        /// </summary>
        public int BitRate { get; private set; }

        /// <summary>
        /// Raw frame data (includes header bytes)
        /// </summary>
        public byte[] RawData { get; private set; }

        /// <summary>
        /// MPEG Version
        /// </summary>
        public MpegVersion MpegVersion { get; private set; }

        /// <summary>
        /// MPEG Layer
        /// </summary>
        public MpegLayer MpegLayer { get; private set; }

        /// <summary>
        /// Channel Mode
        /// </summary>
        public ChannelMode ChannelMode
        {
            get { return channelMode; }
        }

        /// <summary>
        /// The number of samples in this frame
        /// </summary>
        public int SampleCount
        {
            get { return samplesInFrame; }
        }
    }
}
