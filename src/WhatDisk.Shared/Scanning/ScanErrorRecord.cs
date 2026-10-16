namespace WhatDisk.Scanning;

/// <summary>
/// Records a failure and the path at which it occurred.
/// </summary>
/// <param name="Path">The path of the directory that failed.</param>
/// <param name="Error">The failure.</param>
public sealed record ScanErrorRecord(string Path, NodeError Error);
