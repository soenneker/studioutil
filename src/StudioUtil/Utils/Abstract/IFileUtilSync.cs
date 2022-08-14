using System.Collections.Generic;

namespace StudioUtil.Utils.Abstract;

public interface IFileUtilSync
{
    string GetTempFileName();

    string ReadFile(string path);

    byte[] ReadFileToBytes(string path);

    List<string> ReadAllLines(string path);

    void WriteFile(string fullName, string content);

    void WriteAllLines(string path, List<string> content);

    void WriteFile(string path, System.IO.MemoryStream stream);

    void WriteFile(string path, byte[] byteArray);

    bool Exists(string filename);

    void Delete(string filename);

    bool DeleteIfExists(string filename);

    bool TryDelete(string filename);

    void Move(string source, string target);

    void Copy(string source, string target);

    bool TryCopy(string source, string target);
}