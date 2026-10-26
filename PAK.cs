namespace PAKLib
{
    public class PAK
    {
        public PAKData? Data;

        public PAK()
        { }

        public static PAK ReadFromFile(in string FilePath, in string encryption_key = "")
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

            PAK pak = new PAK();

            if (Path.GetExtension(Path.GetFileName(FilePath)).ToLower() == ".epak")
            {
                if(string.IsNullOrEmpty(encryption_key))
                {
                    throw new ArgumentException("Encryption key must be provided for encrypted PAK files.", nameof(encryption_key));
                }

                byte[] encryptedData = File.ReadAllBytes(FilePath);
                Encryption.DecryptBytes(encryptedData, encryption_key);
                pak.Data = PAKData.Read(encryptedData);
            }
            else
            {
                pak.Data = PAKData.Read(File.ReadAllBytes(FilePath));
            }

            return pak;
        }

        public static void SaveToFile(in PAK pak, in string FilePath, in string encryption_key = "")
        {
            if (pak == null || pak.Data == null)
            {
                throw new ArgumentNullException(nameof(pak), "PAK object or its data cannot be null.");
            }
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(FilePath));
            }
            if(string.IsNullOrEmpty(encryption_key) && Path.GetExtension(Path.GetFileName(FilePath)).ToLower() == ".epak")
            {
                throw new ArgumentException("Encryption key must be provided for encrypted PAK files.", nameof(encryption_key));
            }
            pak.Data.Write(FilePath, encryption_key);
        }

        // Salvages the sprites of a damaged PAK (or EPAK) file and writes a rebuilt file to OutputFilePath.
        // Corrupt sprites are dropped, or replaced with a 32x32 placeholder when force is true; check the report.
        public static PAKRepairReport RepairFile(in string FilePath, in string OutputFilePath, in string encryption_key = "", bool force = false)
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

            bool encryptedInput = Path.GetExtension(Path.GetFileName(FilePath)).ToLower() == ".epak";
            bool encryptedOutput = Path.GetExtension(Path.GetFileName(OutputFilePath)).ToLower() == ".epak";
            if ((encryptedInput || encryptedOutput) && string.IsNullOrEmpty(encryption_key))
            {
                throw new ArgumentException("Encryption key must be provided for encrypted PAK files.", nameof(encryption_key));
            }

            byte[] bytes = File.ReadAllBytes(FilePath);
            if (encryptedInput)
            {
                Encryption.DecryptBytes(bytes, encryption_key);
            }

            PAKRepairReport report = PAKRepair.Repair(bytes, force);
            report.Data.Write(OutputFilePath, encryption_key);

            return report;
        }
    }
}
