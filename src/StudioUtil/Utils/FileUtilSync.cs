using System.Collections.Generic;
using System.IO;
using System.Linq;
using StudioUtil.Utils.Abstract;
using Microsoft.Extensions.Logging;

namespace StudioUtil.Utils;

public class FileUtilSync : IFileUtilSync
{
    private readonly ILogger<FileUtilSync> _logger;

    public FileUtilSync(ILogger<FileUtilSync> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Use this instead of Systems.IO.Path.GetTempFileName()! https://stackoverflow.com/a/50413126
    /// 1. It creates 0 byte file (so it'll already exist)
    /// 2. It's slow because it iterates over the file system to (hopefully) find a non-collision
    /// </summary>
    /// <returns></returns>
    public string GetTempFileName()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    }

    public string ReadFile(string path)
    {
        _logger.LogDebug("ReadFile start for {name}", path);

        var result = File.ReadAllText(path);

        _logger.LogDebug("ReadFile completed");

        return result;
    }

    public byte[] ReadFileToBytes(string path)
    {
        _logger.LogDebug("ReadFile start for {name}", path);

        var result = File.ReadAllBytes(path);

        _logger.LogDebug("ReadFile completed");

        return result;
    }

    public List<string> ReadAllLines(string path)
    {
        _logger.LogDebug("ReadFileInLines start for {name}", path);

        List<string> content = File.ReadAllLines(path).ToList();

        _logger.LogDebug("ReadFileInLines completed");

        return content;
    }

    public void WriteAllLines(string path, List<string> lines)
    {
        _logger.LogDebug("WriteAllLines start for {name}", path);

        File.WriteAllLines(path, lines);

        _logger.LogDebug("WriteAllLines completed");
    }

    public void WriteFile(string path, string content)
    {
        _logger.LogDebug("WriteAllTextAsync start for {name}", path);

        File.WriteAllText(path, content);

        _logger.LogDebug("WriteAllTextAsync completed");
    }

    public void WriteFile(string path, System.IO.MemoryStream stream)
    {
        WriteFile(path, stream.ToArray());
    }

    public void WriteFile(string path, byte[] byteArray)
    {
        _logger.LogDebug("WriteAllBytesAsync start for {name}", path);

        File.WriteAllBytes(path, byteArray);

        _logger.LogDebug("WriteAllBytesAsync completed");
    }

    public bool Exists(string filename)
    {
        _logger.LogDebug("Checking if file exists: {file}", filename);

        if (!File.Exists(filename))
        {
            _logger.LogDebug("{file} does not exist", filename);
            return false;
        }

        _logger.LogDebug("File exists: {file}", filename);

        return true;
    }

    public void Delete(string filename)
    {
        _logger.LogDebug("Deleting {file}", filename);

        File.Delete(filename);

        _logger.LogDebug("Finished deleting {file}", filename);
    }

    public bool DeleteIfExists(string filename)
    {
        _logger.LogDebug("Deleting file if it exists: {file}", filename);

        if (Exists(filename))
            return false;

        Delete(filename);

        return true;
    }

    public bool TryDelete(string filename)
    {
        _logger.LogDebug("Trying to delete {file}", filename);
        try
        {
            File.Delete(filename);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception deleting {file}", filename);
            return false;
        }
    }

    public void Move(string source, string target)
    {
        _logger.LogDebug("Moving {source} to {target}", source, target);

        File.Move(source, target);

        _logger.LogDebug("Finished moving {source} to {target}", source, target);
    }

    public void Copy(string source, string target)
    {
        _logger.LogDebug("Copying {source} to {target}", source, target);

        File.Copy(source, target);

        _logger.LogDebug("Finished copying {source} to {target}", source, target);
    }

    public bool TryCopy(string source, string target)
    {
        _logger.LogDebug("Copying {source} to {target}", source, target);

        try
        {
            File.Copy(source, target);
            _logger.LogDebug("Finished copying {source} to {target}", source, target);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception copying {source} to {target}", source, target);
            return false;
        }
    }
}