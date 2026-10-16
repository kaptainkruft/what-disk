namespace WhatDisk.Scanning;

/// <summary>
/// Describes why a directory could not be read completely.
/// </summary>
/// <param name="Kind">The category of the failure.</param>
/// <param name="Message">A human-readable description of the failure.</param>
public sealed record NodeError(ScanErrorKind Kind, string Message);
