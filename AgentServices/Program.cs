using AgentService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging; // ⬅️ Khai báo thêm để nhận các hàm xóa Log gây lỗi
using System;
using System.IO;

namespace AgentServices
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseWindowsService() // Ép chạy ngầm dạng Windows Service
                .ConfigureLogging(logging =>
                {
                    // ⬅️ ĐÂY RỒI FEN: Xóa sạch các bộ ghi mặc định (EventLog gây sập app)
                    logging.ClearProviders();

                    // Chỉ bật ghi log ra màn hình đen Console và cửa sổ Debug của Visual Studio
                    logging.AddConsole();
                    logging.AddDebug();
                })
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                });
    }
}
