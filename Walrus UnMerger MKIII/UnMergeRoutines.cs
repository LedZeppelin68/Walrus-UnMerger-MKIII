using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;

namespace Walrus_UnMerger_MKIII
{
    class UnMergeRoutines
    {
        internal static byte[] sync = { 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 };

        internal static void UnMerge(XmlNode single_file_xml, Dictionary<string, BinaryReader> readers, BinaryWriter FileWriter)
        {
            string type = single_file_xml.Attributes["type"].Value;

            long file_length = Convert.ToInt64(single_file_xml.Attributes["size"].Value);
            long map_offset = Convert.ToInt64(single_file_xml.Attributes["map_offset"].Value);
            long map_length = Convert.ToInt64(single_file_xml.Attributes["map_size"].Value);
            long end_point = map_offset + map_length;
            UInt32 block_offset = 0;

            switch (type)
            {
                case "iso":
                    readers["map"].BaseStream.Seek(map_offset, SeekOrigin.Begin);

                    while (readers["map"].BaseStream.Position != end_point)
                    {
                        byte[] iso_temp = new byte[2048];
                        block_offset = readers["map"].ReadUInt32();

                        if(block_offset != 0xffffffffu)
                        {
                            readers["2048"].BaseStream.Seek(block_offset * 2048, SeekOrigin.Begin);
                            iso_temp = readers["2048"].ReadBytes(2048);
                        }

                        FileWriter.Write(iso_temp);
                    }
                    break;
                case "raw":

                    uint[] edc_lut = new uint[256];
                    byte[] ecc_b_lut = new byte[256];
                    byte[] ecc_f_lut = new byte[256];

                    Init_EDC_ECC_tables(ref edc_lut, ref ecc_b_lut, ref ecc_f_lut);

                    readers["map"].BaseStream.Seek(map_offset, SeekOrigin.Begin);

                    int sector_number = 0;
                    byte[] msf = new byte[3];

                    while (readers["map"].BaseStream.Position != end_point)
                    {
                        byte control = readers["map"].ReadByte();

                        int mode = control & 3;
                        //byte null_flag = (byte)(control & 0x80);
                        //byte last_flag = (byte)(control & 0x40);
                        byte msf_correction = 0;

                        switch (mode)
                        {
                            case 0:
                                byte null_flag = (byte)(control & 0x80);
                                byte last_flag = (byte)(control & 0x40);
                                switch (null_flag)
                                {
                                    default:
                                        int block_size = 2352;
                                        switch (last_flag)
                                        {
                                            case 0x40:
                                                block_size = readers["map"].ReadInt16();
                                                break;
                                        }
                                        block_offset = readers["map"].ReadUInt32();
                                        readers["pcm"].BaseStream.Seek(44 + (block_offset * 2352), SeekOrigin.Begin);
                                        byte[] audio = readers["pcm"].ReadBytes(block_size);
                                        FileWriter.Write(audio);

                                        //switch (file_length > 2352)
                                        //{
                                        //    case true:
                                        //        block_offset = readers["map"].ReadUInt32();
                                        //        readers["pcm"].BaseStream.Seek(44 + (block_offset * 2352), SeekOrigin.Begin);
                                        //        byte[] audio = readers["pcm"].ReadBytes(2352);
                                        //        FileWriter.Write(audio);
                                        //        file_length -= 2352;
                                        //        if(file_length < 100000)
                                        //        {
                                        //            int y = 0;
                                        //        }
                                        //        break;
                                        //    case false:
                                        //        block_offset = readers["map"].ReadUInt32();
                                        //        readers["pcm"].BaseStream.Seek(44 + (block_offset * 2352), SeekOrigin.Begin);
                                        //        byte[] audio_last = readers["pcm"].ReadBytes((int)file_length);
                                        //        FileWriter.Write(audio_last);
                                        //        break;
                                        //}
                                        break;
                                    case 0x80:
                                        UInt32 null_samples = readers["map"].ReadUInt32();
                                        for (int x = 0; x < null_samples; x++)
                                        {
                                            FileWriter.Write(new byte[4]);
                                        }
                                        file_length -= null_samples * 4;
                                        break;
                                }
                                break;
                            case 1:
                                msf_correction = (byte)(control & 0x20);
                                byte[] mode1 = new byte[2352];
                                if (msf_correction != 0) sector_number = readers["map"].ReadInt32();
                                block_offset = readers["map"].ReadUInt32();
                                //block_offset = block_offset * 2048;
                                readers["2048"].BaseStream.Seek(block_offset * 2048, SeekOrigin.Begin);

                                byte[] datamode1 = readers["2048"].ReadBytes(2048);

                                msf = GetMSF(sector_number);
                                InsertChunk(ref mode1, ref sync, 0);
                                InsertChunk(ref mode1, ref msf, 12);
                                mode1[15] = 1;
                                InsertChunk(ref mode1, ref datamode1, 16);

                                calculate_edc(ref mode1, 1, ref edc_lut);
                                calculate_eccp(ref mode1, ref ecc_f_lut, ref ecc_b_lut);
                                calculate_eccq(ref mode1, ref ecc_f_lut, ref ecc_b_lut);

                                FileWriter.Write(mode1);
                                sector_number++;
                                break;
                            case 2:
                                int ecc_error = control & 0x80;
                                int null_edc = control & 0x40;
                                msf_correction = (byte)(control & 0x20);
                                if (msf_correction != 0) sector_number = readers["map"].ReadInt32();
                                byte[] temp = new byte[2352];
                                byte[] subheader = readers["map"].ReadBytes(8);
                                block_offset = readers["map"].ReadUInt32();

                                msf = GetMSF(sector_number);

                                InsertChunk(ref temp, ref sync, 0);
                                
                                InsertChunk(ref temp, ref subheader, 16);

                                switch (subheader[2] & 0x20)
                                {
                                    default:
                                        if (block_offset != 0xffffffffu)
                                        {
                                            block_offset = block_offset * 2048;
                                            readers["2048"].BaseStream.Seek(block_offset, SeekOrigin.Begin);
                                            byte[] dataform1 = readers["2048"].ReadBytes(2048);

                                            InsertChunk(ref temp, ref dataform1, 24);
                                        }
                                        break;
                                    case 0x20:
                                        if (block_offset != 0xffffffffu)
                                        {
                                            block_offset = block_offset * 2324;
                                            readers["2324"].BaseStream.Seek(block_offset, SeekOrigin.Begin);
                                            byte[] dataform2 = readers["2324"].ReadBytes(2324);

                                            InsertChunk(ref temp, ref dataform2, 24);
                                        }
                                        break;
                                }

                                switch (subheader[2] & 0x20)
                                {
                                    default:
                                        if (ecc_error != 0x80)
                                        {
                                            calculate_edc(ref temp, 21, ref edc_lut);
                                            calculate_eccp(ref temp, ref ecc_f_lut, ref ecc_b_lut);
                                            calculate_eccq(ref temp, ref ecc_f_lut, ref ecc_b_lut);
                                            InsertChunk(ref temp, ref msf, 12);
                                            temp[15] = 2;
                                        }
                                        else
                                        {
                                            InsertChunk(ref temp, ref msf, 12);
                                            temp[15] = 2;
                                            calculate_edc(ref temp, 21, ref edc_lut);
                                            calculate_eccp(ref temp, ref ecc_f_lut, ref ecc_b_lut);
                                            calculate_eccq(ref temp, ref ecc_f_lut, ref ecc_b_lut);
                                        }
                                        break;
                                    case 0x020:
                                        if (null_edc != 0x40)
                                        {
                                            calculate_edc(ref temp, 22, ref edc_lut);
                                        }
                                        InsertChunk(ref temp, ref msf, 12);
                                        temp[15] = 2;
                                        break;
                                }

                                FileWriter.Write(temp);
                                sector_number++;
                                break;
                        }
                    }

                    break;
                case "file":
                    long even_block_count = Convert.ToInt64(single_file_xml.Attributes["size"].Value) / 2048;
                    long last_block_count = Convert.ToInt64(single_file_xml.Attributes["size"].Value) % 2048;

                    //long block_offset = 0;
                    readers["map"].BaseStream.Seek(map_offset, SeekOrigin.Begin);

                    for (int i = 0; i < even_block_count; i++)
                    {
                        byte[] even_temp = new byte[2048];
                        block_offset = readers["map"].ReadUInt32();
                        if (block_offset != 0xffffffffu)
                        {
                            readers["2048"].BaseStream.Seek(block_offset * 2048, SeekOrigin.Begin);
                            even_temp = readers["2048"].ReadBytes(2048);
                        }
                        FileWriter.Write(even_temp);
                    }

                    readers["2048"].BaseStream.Seek(readers["map"].ReadUInt32() * 2048, SeekOrigin.Begin);
                    byte[] last_temp = readers["2048"].ReadBytes((Int32)last_block_count);
                    FileWriter.Write(last_temp);

                    break;
            }

        }

        private static void calculate_edc(ref byte[] temp, int mode, ref uint[] edc_lut)
        {
            UInt32 edc = 0;
            int count = 0;
            var i = 0;
            int offset = 0;

            switch (mode)
            {
                case 1:
                    count = 2064;
                    offset = 0;
                    break;
                case 21:
                    count = 2048 + 8;
                    offset = 16;
                    break;
                case 22:
                    count = 2324 + 8;
                    offset = 16;
                    break;
            }
            while (i != count)
            {
                edc = (UInt32)((edc >> 8) ^ edc_lut[(edc ^ (temp[offset + i++])) & 0xff]);
            }
            byte[] ar_edc = BitConverter.GetBytes(edc);

            for (i = 0; i < 4; i++)
            {
                temp[i + offset + count] = ar_edc[i];
            }
        }

        private static void calculate_eccq(ref byte[] buffer, ref byte[] ecc_f_lut, ref byte[] ecc_b_lut)
        {
            UInt32 major_count, minor_count, major_mult, minor_inc;
            major_count = 52;
            minor_count = 43;
            major_mult = 86;
            minor_inc = 88;

            var eccsize = major_count * minor_count;
            UInt32 major, minor;
            for (major = 0; major < major_count; major++)
            {
                var index = (major >> 1) * major_mult + (major & 1);
                byte ecc_a = 0;
                byte ecc_b = 0;
                for (minor = 0; minor < minor_count; minor++)
                {
                    byte temp = buffer[12 + index];
                    index += minor_inc;
                    if (index >= eccsize) index -= eccsize;
                    ecc_a ^= temp;
                    ecc_b ^= temp;
                    ecc_a = ecc_f_lut[ecc_a];
                }
                ecc_a = ecc_b_lut[ecc_f_lut[ecc_a] ^ ecc_b];
                buffer[2076 + 172 + major] = ecc_a;
                buffer[2076 + 172 + major + major_count] = (byte)(ecc_a ^ ecc_b);
            }
        }

        private static void calculate_eccp(ref byte[] buffer, ref byte[] ecc_f_lut, ref byte[] ecc_b_lut)
        {
            UInt32 major_count, minor_count, major_mult, minor_inc;
            major_count = 86;
            minor_count = 24;
            major_mult = 2;
            minor_inc = 86;

            var eccsize = major_count * minor_count;
            UInt32 major, minor;
            for (major = 0; major < major_count; major++)
            {
                var index = (major >> 1) * major_mult + (major & 1);
                byte ecc_a = 0;
                byte ecc_b = 0;
                for (minor = 0; minor < minor_count; minor++)
                {
                    byte temp = buffer[12 + index];
                    index += minor_inc;
                    if (index >= eccsize) index -= eccsize;
                    ecc_a ^= temp;
                    ecc_b ^= temp;
                    ecc_a = ecc_f_lut[ecc_a];
                }
                ecc_a = ecc_b_lut[ecc_f_lut[ecc_a] ^ ecc_b];
                buffer[2076 + major] = ecc_a;
                buffer[2076 + major + major_count] = (byte)(ecc_a ^ ecc_b);
            }
        }

        private static void Init_EDC_ECC_tables(ref uint[] edc_lut, ref byte[] ecc_b_lut, ref byte[] ecc_f_lut)
        {
            UInt32 k, l, m;

            for (k = 0; k < 256; k++)
            {
                l = (UInt32)((k << 1) ^ ((k & 0x80) != 0 ? 0x11d : 0));
                ecc_f_lut[k] = (byte)l;
                ecc_b_lut[k ^ l] = (byte)k;
                m = k;

                for (l = 0; l < 8; l++)
                {
                    m = (m >> 1) ^ ((m & 1) != 0 ? 0xd8018001 : 0);
                }
                edc_lut[k] = m;
            }
        }

        internal static byte[] msf_table = {
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19,
            0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29,
            0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
            0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
            0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99,
            0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9,
            0xb0, 0xb1, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9,
            0xc0, 0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9,
            0xd0, 0xd1, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9,
            0xe0, 0xe1, 0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9,
            0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9
        };

        private static byte[] GetMSF(int sector_number)
        {
            byte[] msf = new byte[3];
            int minutes = sector_number / 4500;
            int seconds = sector_number % 4500 / 75;
            int frames = sector_number % 75;

            msf[0] = msf_table[minutes];
            msf[1] = msf_table[seconds];
            msf[2] = msf_table[frames];

            return msf;
        }

        private static void InsertChunk(ref byte[] temp, ref byte[] insertion, int offset)
        {
            for (int i = 0; i < insertion.Length; i++)
            {
                temp[i + offset] = insertion[i];
            }
        }
    }
}
