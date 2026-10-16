using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhatDisk.MacOS.Interop;

/// <summary>
/// Minimal access to the Objective-C runtime, enough to call a handful of AppKit methods.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class ObjectiveC
{
    /// <summary>The Objective-C runtime library.</summary>
    private const string Library = "/usr/lib/libobjc.A.dylib";

    /// <summary>
    /// Looks up a class by name.
    /// </summary>
    /// <param name="name">The class name.</param>
    /// <returns>The class, or zero.</returns>
    [LibraryImport(Library, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetClass(string name);

    /// <summary>
    /// Registers (or looks up) a selector.
    /// </summary>
    /// <param name="name">The selector name.</param>
    /// <returns>The selector.</returns>
    [LibraryImport(Library, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Selector(string name);

    /// <summary>
    /// Sends a message with no arguments that returns an object.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector);

    /// <summary>
    /// Sends a message with one object argument that returns an object.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="argument">The argument.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, nint argument);

    /// <summary>
    /// Sends a message with a pointer and two object arguments that returns an object.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="pointer">The pointer argument.</param>
    /// <param name="second">The second argument.</param>
    /// <param name="third">The third argument.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, void* pointer, nint second, nint third);

    /// <summary>
    /// Sends a message with an unsigned integer and an object argument that returns an object.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="value">The integer argument.</param>
    /// <param name="argument">The object argument.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, nuint value, nint argument);

    /// <summary>
    /// Sends a message with a C string argument that returns an object.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <param name="utf8">A null-terminated UTF-8 string.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nint Send(nint receiver, nint selector, byte* utf8);

    /// <summary>
    /// Sends a message with no arguments that returns an unsigned integer.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The result.</returns>
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    internal static partial nuint SendForUnsigned(nint receiver, nint selector);

    /// <summary>
    /// Pushes an autorelease pool, so objects autoreleased on background threads are freed.
    /// </summary>
    /// <returns>A token for <see cref="AutoreleasePoolPop"/>.</returns>
    [LibraryImport(Library, EntryPoint = "objc_autoreleasePoolPush")]
    internal static partial nint AutoreleasePoolPush();

    /// <summary>
    /// Pops an autorelease pool, releasing the objects autoreleased since the matching push.
    /// </summary>
    /// <param name="pool">The token from <see cref="AutoreleasePoolPush"/>.</param>
    [LibraryImport(Library, EntryPoint = "objc_autoreleasePoolPop")]
    internal static partial void AutoreleasePoolPop(nint pool);

    /// <summary>
    /// Creates an autoreleased <c>NSString</c>.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <returns>The <c>NSString</c>, or zero.</returns>
    internal static nint CreateString(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* pointer = bytes)
        {
            return Send(GetClass("NSString"), Selector("stringWithUTF8String:"), pointer);
        }
    }
}

/// <summary>
/// A Core Graphics rectangle, passed by pointer to AppKit.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The bottom edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct CGRect(double X, double Y, double Width, double Height);
