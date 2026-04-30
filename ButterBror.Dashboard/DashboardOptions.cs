namespace ButterBror.Dashboard;

public class DashboardOptions
{
    public bool Enable { get; set; } = true;
    public int Port { get; set; } = 5000;
    public string AccessToken { get; set; } = string.Empty;
    public int MaxLogBufferSize { get; set; } = 500;
    public string Address { get; set; } = "127.0.0.1";
}
