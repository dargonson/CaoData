using System.ComponentModel;

namespace AgentService
{
    internal static class MsiExitCodeCatalog
    {
        public static string GetVietnameseMessage(int code) => code switch
        {
            0 => "Cài đặt hoàn thành thành công.",
            13 => "Dữ liệu bộ cài không hợp lệ.",
            87 => "Một tham số truyền cho Windows Installer không hợp lệ.",
            120 => "Custom action gọi một chức năng không được Windows Installer hỗ trợ.",
            1259 => "Windows đã chặn bộ cài do vấn đề tương thích.",
            1601 => "Không truy cập được dịch vụ Windows Installer.",
            1602 => "Quá trình cài đặt đã bị hủy.",
            1603 => "Lỗi nghiêm trọng trong quá trình cài đặt. Hãy xem log MSI để biết bước gây lỗi.",
            1604 => "Quá trình cài đặt bị tạm dừng và chưa hoàn tất.",
            1605 => "Thao tác này chỉ hợp lệ với sản phẩm đang được cài trên máy.",
            1606 => "Tính năng được yêu cầu chưa được đăng ký.",
            1607 => "Thành phần được yêu cầu chưa được đăng ký.",
            1608 => "Thuộc tính Windows Installer không xác định.",
            1609 => "Trạng thái handle Windows Installer không hợp lệ.",
            1610 => "Dữ liệu cấu hình của sản phẩm đã hỏng.",
            1611 => "Không tìm thấy component qualifier.",
            1612 => "Không tìm thấy hoặc không truy cập được nguồn cài đặt.",
            1613 => "Gói cài đặt yêu cầu phiên bản Windows Installer mới hơn.",
            1614 => "Sản phẩm hiện đã được gỡ cài đặt.",
            1615 => "Cú pháp truy vấn SQL trong gói MSI không hợp lệ hoặc không được hỗ trợ.",
            1616 => "Không tồn tại trường dữ liệu được yêu cầu trong bản ghi MSI.",
            1618 => "Một tiến trình cài đặt khác đang chạy. Hãy hoàn tất hoặc dừng tiến trình đó trước.",
            1619 => "Không mở được gói cài đặt. Hãy kiểm tra đường dẫn, quyền truy cập và file MSI.",
            1620 => "Không mở được gói cài đặt. Gói MSI có thể không hợp lệ hoặc đã hỏng.",
            1621 => "Không khởi tạo được giao diện của dịch vụ Windows Installer.",
            1622 => "Không mở được file log cài đặt. Hãy kiểm tra thư mục và quyền ghi.",
            1623 => "Ngôn ngữ của gói cài đặt không được hệ thống hỗ trợ.",
            1624 => "Không áp dụng được transform. Hãy kiểm tra đường dẫn và nội dung transform.",
            1625 => "Chính sách hệ thống không cho phép cài đặt gói này.",
            1626 => "Không thể thực thi chức năng Windows Installer được yêu cầu.",
            1627 => "Chức năng Windows Installer bị lỗi khi thực thi.",
            1628 => "Gói MSI chỉ định bảng không hợp lệ hoặc không xác định.",
            1629 => "Dữ liệu truyền vào Windows Installer không đúng kiểu.",
            1630 => "Kiểu dữ liệu này không được Windows Installer hỗ trợ.",
            1631 => "Không khởi động được dịch vụ Windows Installer.",
            1632 => "Thư mục Temp đầy hoặc Agent không có quyền ghi vào thư mục Temp.",
            1633 => "Gói cài đặt không hỗ trợ nền tảng hoặc kiến trúc Windows hiện tại.",
            1634 => "Thành phần này không được sử dụng trên máy hiện tại.",
            1635 => "Không mở được gói bản vá. Hãy kiểm tra file và quyền truy cập.",
            1636 => "Gói bản vá không hợp lệ hoặc đã hỏng.",
            1637 => "Gói bản vá yêu cầu phiên bản Windows Installer mới hơn.",
            1638 => "Máy đã cài một phiên bản khác của sản phẩm này. Không thể tiếp tục cài phiên bản hiện tại.",
            1639 => "Tham số dòng lệnh Windows Installer không hợp lệ.",
            1640 => "Tài khoản hiện tại không được phép cài đặt từ client session của Terminal Server.",
            1641 => "Cài đặt thành công và Windows Installer đã yêu cầu khởi động lại máy.",
            1642 => "Không tìm thấy áp dụng bản vá vì Không tìm thấy sản phẩm hoặc phiên bản đích phù hợp.",
            1643 => "Chính sách hệ thống không cho phép cài đặt gói bản vá.",
            1644 => "Chính sách hệ thống không cho phép một hoặc nhiều tùy chỉnh transform.",
            1645 => "Windows Installer không cho phép thao tác cài đặt này từ Remote Desktop.",
            1646 => "Gói bản vá này không hỗ trợ gỡ bỏ.",
            1647 => "Bản vá chưa được áp dụng cho sản phẩm này.",
            1648 => "Không tìm thấy thứ tự áp dụng hợp lệ cho nhóm bản vá.",
            1649 => "Chính sách hệ thống không cho phép gỡ bản vá.",
            1650 => "Dữ liệu XML của bản vá không hợp lệ.",
            1651 => "Không áp dụng được bản vá quản trị cho sản phẩm đang ở trạng thái advertised.",
            1652 => "Windows Installer không hoạt động trong Safe Mode.",
            1653 => "Không thể chạy giao dịch nhiều gói vì rollback đã bị tắt.",
            1654 => "Ứng dụng không được hỗ trợ trên phiên bản Windows này hoặc chữ ký không phù hợp với máy ARM.",
            3010 => "Cài đặt thành công nhưng cần khởi động lại Windows để hoàn tất.",
            _ => GetWindowsMessage(code)
        };

        private static string GetWindowsMessage(int code)
        {
            string systemMessage = new Win32Exception(code).Message;
            return string.IsNullOrWhiteSpace(systemMessage)
                ? "Windows Installer trả về mã chưa được nhận diện."
                : systemMessage;
        }
    }
}
