namespace PAKLib
{
    public class PAK
    {
        public PAKData? Data;

        public PAK()
        { }

        public static PAK ReadFromFile(in string FilePath)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(FilePath));
            }

            if(!Path.IsPathRooted(FilePath))
            {
                throw new ArgumentException("File path must be an absolute path.", nameof(FilePath));
            }

            if (!File.Exists(FilePath))
            {
                throw new FileNotFoundException("The specified file does not exist.", FilePath);
            }

            if (Path.GetExtension(Path.GetFileName(FilePath)).ToLower() == ".epak")
            {
                throw new NotSupportedException("Encrypted .epak files are no longer supported (removed in PAKLib 2.0).");
            }

            PAK pak = new PAK();
            pak.Data = PAKData.Read(File.ReadAllBytes(FilePath));

            return pak;
        }

        public static void SaveToFile(in PAK pak, in string FilePath)
        {
            if (pak == null || pak.Data == null)
            {
                throw new ArgumentNullException(nameof(pak), "PAK object or its data cannot be null.");
            }
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(FilePath));
            }
            pak.Data.Write(FilePath);
        }

        // Salvages the sprites of a damaged PAK file and writes a rebuilt file to OutputFilePath.
        // Corrupt sprites are dropped, or replaced with a 32x32 placeholder when force is true; check the report.
        public static PAKRepairReport RepairFile(in string FilePath, in string OutputFilePath, bool force = false)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(FilePath));
            }
            if (!Path.IsPathRooted(FilePath))
            {
                throw new ArgumentException("File path must be an absolute path.", nameof(FilePath));
            }
            if (!File.Exists(FilePath))
            {
                throw new FileNotFoundException("The specified file does not exist.", FilePath);
            }
            if (string.IsNullOrWhiteSpace(OutputFilePath))
            {
                throw new ArgumentException("Output file path cannot be null or empty.", nameof(OutputFilePath));
            }
            if (string.Equals(Path.GetFullPath(FilePath), Path.GetFullPath(OutputFilePath), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Output file path must differ from the input file path.", nameof(OutputFilePath));
            }

            PAKRepairReport report = PAKRepair.Repair(File.ReadAllBytes(FilePath), force);
            report.Data.Write(OutputFilePath);

            return report;
        }
    }
}
