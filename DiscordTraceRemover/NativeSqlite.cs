using System.Runtime.InteropServices;

namespace DiscordTraceRemover;

internal static class NativeSqlite
{
    private const int Ok = 0;
    private const int CheckpointTruncate = 3;

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open16(nint fileName, out nint database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(nint database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(
        nint database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        nint callback,
        nint argument,
        out nint errorMessage);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(nint pointer);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_total_changes(nint database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sqlite3_errmsg(nint database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_wal_checkpoint_v2(
        nint database,
        nint databaseName,
        int mode,
        out int logFrames,
        out int checkpointedFrames);

    internal static int Execute(string databasePath, string sql)
    {
        nint database = 0;
        var pathPointer = Marshal.StringToHGlobalUni(databasePath);
        try
        {
            var openResult = sqlite3_open16(pathPointer, out database);
            if (openResult != Ok)
            {
                var details = database == 0
                    ? null
                    : Marshal.PtrToStringUTF8(sqlite3_errmsg(database));
                throw new InvalidOperationException(
                    $"Could not open browser database (SQLite error {openResult}: {details ?? "unknown error"}).");
            }

            var result = sqlite3_exec(database, sql, 0, 0, out var errorPointer);
            if (result != Ok)
            {
                var error = errorPointer == 0
                    ? $"SQLite error {result}"
                    : Marshal.PtrToStringUTF8(errorPointer) ?? $"SQLite error {result}";

                if (errorPointer != 0)
                {
                    sqlite3_free(errorPointer);
                }

                sqlite3_exec(database, "ROLLBACK;", 0, 0, out var rollbackError);
                if (rollbackError != 0)
                {
                    sqlite3_free(rollbackError);
                }

                throw new InvalidOperationException(error);
            }

            var changes = sqlite3_total_changes(database);
            var checkpointResult = sqlite3_wal_checkpoint_v2(
                database,
                0,
                CheckpointTruncate,
                out var logFrames,
                out var checkpointedFrames);
            if (checkpointResult != Ok || (logFrames > 0 && checkpointedFrames < logFrames))
            {
                var details = Marshal.PtrToStringUTF8(sqlite3_errmsg(database));
                throw new InvalidOperationException(
                    $"Could not finalize the browser database safely (SQLite error {checkpointResult}: {details ?? "unknown error"}).");
            }

            return changes;
        }
        finally
        {
            if (database != 0)
            {
                sqlite3_close(database);
            }

            Marshal.FreeHGlobal(pathPointer);
        }
    }
}
