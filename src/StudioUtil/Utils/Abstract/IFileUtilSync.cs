using System.Collections.Generic;

namespace StudioUtil.Utils.Abstract;

/// <summary>
/// Defines the file util sync contract.
/// </summary>
public interface IFileUtilSync
{
    /// <summary>
    /// Use this instead of Systems.IO.Path.GetTempFileName()! https://stackoverflow.com/a/50413126
    /// 1. It creates 0 byte file (so it'll already exist)
    /// 2. It's slow because it iterates over the file system to (hopefully) find a non-collision
    /// </summary>
    /// <returns></returns>
    string GetTempFileName();

    /// <summary>
    /// Reads file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The result of the operation.</returns>
    string ReadFile(string path);

    /// <summary>
    /// Reads file to bytes.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The result of the operation.</returns>
    byte[] ReadFileToBytes(string path);

    /// <summary>
    /// Reads all lines.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The result of the operation.</returns>
    List<string> ReadAllLines(string path);

    /// <summary>
    /// Writes file.
    /// </summary>
    /// <param name="fullName">The full name.</param>
    /// <param name="content">The content.</param>
    void WriteFile(string fullName, string content);

    /// <summary>
    /// Writes all lines.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="content">The content.</param>
    void WriteAllLines(string path, List<string> content);

    /// <summary>
    /// Writes file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="stream">The stream.</param>
    void WriteFile(string path, System.IO.MemoryStream stream);

    /// <summary>
    /// Writes file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="byteArray">The byte array.</param>
    void WriteFile(string path, byte[] byteArray);

    /// <summary>
    /// Executes the exists operation.
    /// </summary>
    /// <param name="filename">The filename.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool Exists(string filename);

    /// <summary>
    /// Executes the delete operation.
    /// </summary>
    /// <param name="filename">The filename.</param>
    void Delete(string filename);

    /// <summary>
    /// Deletes if exists.
    /// </summary>
    /// <param name="filename">The filename.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool DeleteIfExists(string filename);

    /// <summary>
    /// Attempts to execute delete.
    /// </summary>
    /// <param name="filename">The filename.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool TryDelete(string filename);

    /// <summary>
    /// Executes the move operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    void Move(string source, string target);

    /// <summary>
    /// Executes the copy operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    void Copy(string source, string target);

    /// <summary>
    /// Attempts to execute copy.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
    bool TryCopy(string source, string target);
}