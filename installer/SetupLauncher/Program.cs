using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace DeepSeekWidgetSetup;

/// <summary>
/// 自解压安装启动器：把内嵌的 MSIX / 证书 / 安装脚本释放到临时目录，
/// 再调用 PowerShell 执行安装脚本（安装证书 + 安装/升级 MSIX + 启动）。
/// 不依赖 IExpress/wextract，避免其 CAB 解包在部分系统上失败（簇大小、CreateProcess 等错误）。
/// </summary>
internal static class Program
{
    private static int Main()
    {
        try
        {
            Console.Title = "DeepSeek 峰谷计价小组件 安装程序";
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("DeepSeek 峰谷计价小组件 · 安装程序");
            Console.WriteLine("==========================================");

            // 依次尝试多个可写目录（某些系统/安全策略会限制 TEMP 下新建目录）
            var workDir = CreateWorkDirectory();
            Console.WriteLine($"解压到：{workDir}");

            var assembly = Assembly.GetExecutingAssembly();
            var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                string? fileName = null;
                if (resourceName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = "DeepSeekPeakWidget.msix";
                }
                else if (resourceName.EndsWith(".cer", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = "pve-widget-cert.cer";
                }
                else if (resourceName.EndsWith("DeepSeekWidget-Setup.ps1", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = "DeepSeekWidget-Setup.ps1";
                }

                if (fileName is null)
                {
                    continue;
                }

                var target = Path.Combine(workDir, fileName);
                using var input = assembly.GetManifestResourceStream(resourceName);
                if (input is null)
                {
                    continue;
                }
                using var output = File.Create(target);
                input.CopyTo(output);
                extracted[fileName] = target;
                Console.WriteLine($"  释放：{fileName}（{new FileInfo(target).Length / 1024.0 / 1024.0:N1} MB）");
            }

            if (!extracted.ContainsKey("DeepSeekWidget-Setup.ps1") || !extracted.ContainsKey("DeepSeekPeakWidget.msix"))
            {
                Console.WriteLine("错误：内置安装文件缺失，安装包可能已损坏，请重新下载。");
                Pause();
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("开始安装（安装证书 → 安装/升级小组件 → 启动）…");
            Console.WriteLine("------------------------------------------");

            var setupScript = extracted["DeepSeekWidget-Setup.ps1"];
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{setupScript}\"",
                WorkingDirectory = workDir,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                Console.WriteLine("错误：无法启动 PowerShell 安装脚本。");
                Pause();
                return 1;
            }

            process.WaitForExit();
            var exitCode = process.ExitCode;

            Console.WriteLine("------------------------------------------");
            Console.WriteLine(exitCode == 0
                ? "安装完成。"
                : $"安装过程结束，退出码 {exitCode}，请查看上方提示与 %TEMP%\\DeepSeekPeakWidget-setup.log。");

            TryCleanup(workDir);
            Pause();
            return exitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine("安装失败：" + ex.Message);
            Pause();
            return 1;
        }
    }

    /// <summary>在若干候选目录下创建本次安装的临时工作目录，返回第一个成功的路径。</summary>
    private static string CreateWorkDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(Path.GetTempPath(), "DeepSeekWidgetSetup"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepSeekWidgetSetup"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DeepSeekWidgetSetup"),
        };

        var errors = new List<string>();
        foreach (var root in candidates)
        {
            try
            {
                var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch (Exception ex)
            {
                errors.Add($"{root} -> {ex.GetType().Name}: {ex.Message}");
            }
        }

        throw new IOException("无法创建临时目录：" + string.Join("；", errors));
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.WriteLine("按任意键关闭窗口…");
        try { Console.ReadKey(true); } catch { }
    }

    private static void TryCleanup(string workDir)
    {
        try
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
        catch
        {
            // 文件被占用时留给系统临时目录清理
        }
    }
}
