using System.ComponentModel;
using System.Text.Json;

namespace Tack.Core.Updates;

/// <summary>
/// What the person reads when a check or install fails: one of five fixed
/// messages, never an error's own text, which can carry a URL or a path.
/// The details go to the trace.
/// </summary>
public static class UpdateFailures
{
    public const string Network = "Couldn't reach the update server. Check your internet connection.";
    public const string Server = "The update server didn't give a usable answer. Try again later.";
    public const string Signature = "The update's signature could not be verified, so it was not installed.";
    public const string Local = "Couldn't prepare the update on this PC.";
    public const string Other = "The update could not be completed.";

    /// <summary>The message for <paramref name="kind"/>.</summary>
    public static string Words(UpdateFailure kind) => kind switch
    {
        UpdateFailure.Network => Network,
        UpdateFailure.Server => Server,
        UpdateFailure.Signature => Signature,
        UpdateFailure.Local => Local,
        _ => Other,
    };

    /// <summary>
    /// Sorts what was thrown into the kinds the person is told apart: a
    /// connection that failed or timed out is the network; an answer that is
    /// not an update is the server's; a file or a program that would not
    /// start is this PC's.
    /// </summary>
    public static UpdateFailure Classify(Exception error) => error switch
    {
        UpdateFailureException known => known.Kind,
        // HttpIOException, a connection lost mid-download, is an IOException too.
        HttpRequestException or HttpIOException or OperationCanceledException or TimeoutException => UpdateFailure.Network,
        JsonException => UpdateFailure.Server,
        IOException or UnauthorizedAccessException or Win32Exception => UpdateFailure.Local,
        _ => UpdateFailure.Other,
    };
}
