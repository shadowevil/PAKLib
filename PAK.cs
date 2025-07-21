namespace PAKLib
{
    public class PAK
    {
        public Data? Data;

        public PAK()
        { }

        public static PAK CreateFromFile(in string FilePath)
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
            pak.Data = Data.Create(File.ReadAllBytes(FilePath));

            return pak;
        }
    }
}
