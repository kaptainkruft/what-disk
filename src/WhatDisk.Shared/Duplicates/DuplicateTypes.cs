namespace WhatDisk.Duplicates;

/// <summary>
/// A file to consider in a duplicate search.
/// </summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Length">The file's length in bytes, as recorded by the scan.</param>
public sealed record DuplicateCandidate(string Path, long Length);

/// <summary>
/// How the files of a set were found to be the same.
/// </summary>
public enum DuplicateComparison
{
    /// <summary>The whole of each file was hashed, and the hashes are equal.</summary>
    Hash,

    /// <summary>
    /// Samples of each file (<see cref="DuplicateFinderOptions.SampleCount"/> of
    /// <see cref="DuplicateFinderOptions.SampleLength"/> bytes, spread from start to end) are equal; the rest was not
    /// compared.
    /// </summary>
    Samples,

    /// <summary>
    /// Only the lengths are equal, as recorded by the scan; the contents were not compared, so the files may well
    /// differ.
    /// </summary>
    Size,
}

/// <summary>
/// Files with the same contents, as far as they were compared.
/// </summary>
/// <param name="Id">The set's number, from 1; sets are numbered by the space they waste, largest first.</param>
/// <param name="Length">The length of each file in bytes.</param>
/// <param name="Hash">
/// The contents' XXH128 hash as 32 lower-case hexadecimal digits, or <see langword="null"/> when the files were only
/// sampled or only their lengths compared.
/// </param>
/// <param name="Paths">The files' paths, sorted; always at least two.</param>
/// <param name="Comparison">How the files were compared.</param>
public sealed record DuplicateSet(int Id, long Length, string? Hash, IReadOnlyList<string> Paths, DuplicateComparison Comparison)
{
    /// <summary>
    /// Gets the space that keeping only one of the files would free.
    /// </summary>
    public long WastedBytes => Length * (Paths.Count - 1);
}

/// <summary>
/// Files of a sampled set whose hashes turned out equal.
/// </summary>
/// <param name="Hash">The contents' XXH128 hash as 32 lower-case hexadecimal digits.</param>
/// <param name="Paths">The files' paths, sorted; always at least two.</param>
public sealed record DuplicateHashGroup(string Hash, IReadOnlyList<string> Paths);

/// <summary>
/// The outcome of hashing the files of a sampled set.
/// </summary>
/// <param name="Groups">The groups of identical files, largest first; none when no two files are identical.</param>
/// <param name="Failures">The files that could not be read or changed since the search.</param>
public sealed record DuplicateVerification(IReadOnlyList<DuplicateHashGroup> Groups, IReadOnlyList<DuplicateFailure> Failures);

/// <summary>
/// A file that could not be compared.
/// </summary>
/// <param name="Path">The file's path.</param>
/// <param name="Reason">Why it could not be compared.</param>
public sealed record DuplicateFailure(string Path, string Reason);

/// <summary>
/// The stages of a duplicate search.
/// </summary>
public enum DuplicateSearchStage
{
    /// <summary>Listing the scanned files and grouping them by length.</summary>
    Grouping,

    /// <summary>
    /// Comparing samples of each file larger than <see cref="DuplicateFinderOptions.FullHashLimit"/> that shares its
    /// length with another.
    /// </summary>
    Sampling,

    /// <summary>
    /// Hashing the whole of each file up to <see cref="DuplicateFinderOptions.FullHashLimit"/> that shares its length
    /// with another.
    /// </summary>
    Hashing,
}

/// <summary>
/// How far a duplicate search has got.
/// </summary>
/// <param name="Stage">The stage in progress.</param>
/// <param name="FilesDone">The files finished in this stage.</param>
/// <param name="FilesTotal">The files in this stage; while grouping, the files seen so far.</param>
/// <param name="BytesDone">The bytes hashed so far (hashing only).</param>
/// <param name="BytesTotal">The bytes to hash (hashing only).</param>
public sealed record DuplicateProgress(DuplicateSearchStage Stage, int FilesDone, int FilesTotal, long BytesDone = 0, long BytesTotal = 0);

/// <summary>
/// The outcome of a duplicate search.
/// </summary>
/// <param name="Sets">The sets of duplicates, largest waste first.</param>
/// <param name="FilesConsidered">The files over the minimum length that were grouped by length.</param>
/// <param name="SizeMatches">The files that share their length with another file.</param>
/// <param name="HashedFiles">The files up to <see cref="DuplicateFinderOptions.FullHashLimit"/> that were hashed.</param>
/// <param name="SampledFiles">The larger files that were compared by samples.</param>
/// <param name="EmptyFiles">The empty files, which were left out.</param>
/// <param name="OfflineFiles">The files stored only in the cloud, which were left out so as not to download them.</param>
/// <param name="HardLinks">The extra hard links to files already compared, which were left out.</param>
/// <param name="Failures">The files that could not be read or changed during the search.</param>
/// <param name="SmallFiles">
/// The non-empty files no longer than <see cref="DuplicateFinderOptions.MinimumLength"/>, which were left out.
/// </param>
public sealed record DuplicateSearchResult(
    IReadOnlyList<DuplicateSet> Sets,
    int FilesConsidered,
    int SizeMatches,
    int HashedFiles,
    int SampledFiles,
    int EmptyFiles,
    int OfflineFiles,
    int HardLinks,
    IReadOnlyList<DuplicateFailure> Failures,
    int SmallFiles = 0)
{
    /// <summary>
    /// Gets the number of files in all the sets.
    /// </summary>
    public int DuplicateFiles => Sets.Sum(static set => set.Paths.Count);

    /// <summary>
    /// Gets the space that keeping only one file of each set would free.
    /// </summary>
    public long WastedBytes => Sets.Sum(static set => set.WastedBytes);

    /// <summary>
    /// Gets the number of sets that were only sampled.
    /// </summary>
    public int SampledSets => Sets.Count(static set => set.Comparison == DuplicateComparison.Samples);
}
