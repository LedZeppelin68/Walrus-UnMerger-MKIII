using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;

namespace Walrus_UnMerger_MKIII
{
    class Program
    {
        static void Main(string[] args)
        {
            //args = new string[] { @"K:\zeptrades\Tales of Destiny\Tales of Destiny.xml" };

            foreach (string file_xml in args)
            {
                string WorkingDirectory = Path.GetDirectoryName(file_xml);

                XmlDocument control_xml = new XmlDocument();
                control_xml.Load(file_xml);

                XmlNodeList records_xml = control_xml.DocumentElement.SelectNodes("swarm/record");

                int num = 0;
                foreach (XmlNode record_xml in records_xml)
                {
                    string entry = string.Format("[{0}]: {1}", ++num, record_xml.Attributes["name"].Value);
                    Console.WriteLine(entry);
                }

                Console.WriteLine();
                Console.WriteLine("[0]: Exit");
                Console.WriteLine();
                Console.Write("Choose wisely: ");

                ConsoleKeyInfo y = Console.ReadKey();

                if (y.KeyChar == '0')
                {
                    break;
                }

                

                XmlNodeList partitioms_xml = control_xml.DocumentElement.SelectNodes("partition/record");

                Dictionary<string, BinaryReader> Readers = new Dictionary<string, BinaryReader>();

                foreach(XmlNode partition in partitioms_xml)
                {
                    Readers.Add(partition.Attributes["type"].Value, new BinaryReader(new FileStream(Path.Combine(WorkingDirectory, partition.Attributes["name"].Value), FileMode.Open)));
                }

                XmlNode selected = records_xml[y.KeyChar - 48 - 1];

                foreach (XmlNode single_file_xml in selected.SelectNodes("file"))
                {
                    string filename = single_file_xml.Attributes["name"].Value;
                    string fila_md5 = single_file_xml.Attributes["md5"].Value;
                    

                    using (BinaryWriter FileWriter = new BinaryWriter(new FileStream(Path.Combine(WorkingDirectory, filename), FileMode.Create)))
                    {
                        UnMergeRoutines.UnMerge(single_file_xml, Readers, FileWriter);
                    }
                }
            }
        }
    }
}
