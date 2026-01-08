namespace SupportDesk.Api.Paging;

public static class Cursor
{
    public static string Encode(string sortKey)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sortKey));

    public static bool TryDecode(string cursor, out string sortKey)
    {
        sortKey = "";
        try
        {
            sortKey = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return !string.IsNullOrWhiteSpace(sortKey) && sortKey.Contains('|');
        }
        catch
        {
            return false;
        }
    }
}
