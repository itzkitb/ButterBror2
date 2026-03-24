namespace ButterBror.Dashboard;

public class DashboardOptions
{
    public int Port { get; set; } = 5000;
    public string AccessToken { get; set; } = string.Empty;
    public int MaxLogBufferSize { get; set; } = 500;
}
