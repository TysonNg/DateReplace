# HƯỚNG DẪN SỬ DỤNG PHẦN MỀM ANTIGRAVITY CONTRACT DATE REPLACER
### Phiên bản: v1.0.0 (Release Candidate) — Hệ Thống Thay Thế Ngày Hợp Đồng Chuyên Nghiệp

---

## MỤC LỤC
1. [Giới thiệu tổng quan](#1-giới-thiệu-tổng-quan)
2. [Yêu cầu hệ thống](#2-yêu-cầu-hệ-thống)
3. [Quy trình sử dụng cơ bản (A đến Z)](#3-quy-trình-sử-dụng-cơ-bản-a-đến-z)
   - [Bước 1: Chọn thư mục hoặc tập tin hợp đồng Word](#bước-1-chọn-thư-mục-hoặc-tập-tin-hợp-đồng-word)
   - [Bước 2: Quét phân tích & Phân loại ngày tự động](#bước-2-quét-phân-tích--phân-loại-ngày-tự-động)
   - [Bước 3: Kiểm tra & Tùy chỉnh danh sách ngày (Review)](#bước-3-kiểm-tra--tùy-chỉnh-danh-sách-ngày-review)
   - [Bước 4: Áp dụng Mẫu hợp đồng (Presets / Templates)](#bước-4-áp-dụng-mẫu-hợp-đồng-presets--templates)
   - [Bước 5: So sánh trực quan trước khi thay thế (Preview Diff)](#bước-5-so-sánh-trực-quan-trước-khi-thay-thế-preview-diff)
   - [Bước 6: Thực thi thay thế an toàn 100%](#bước-6-thực-thi-thay-thế-an-toàn-100)
   - [Bước 7: Xuất báo cáo nghiệm thu đa định dạng](#bước-7-xuất-báo-cáo-nghiệm-thu-đa-định-dạng)
4. [Các tính năng nâng cao & Chuyên sâu](#4-các-tính-năng-nâng-cao--chuyên-sâu)
   - [Khôi phục dữ liệu tức thì (Backup & Rollback)](#khôi-phục-dữ-liệu-tức-thì-backup--rollback)
   - [Nhật ký kiểm toán chống can thiệp (Audit Log & SHA-256)](#nhật-ký-kiểm-toán-chống-can-thiệp-audit-log--sha-256)
   - [Trí thông minh ngày: Âm - Dương lịch & Ngày lễ Việt Nam](#trí-thông-minh-ngày-âm---dương-lịch--ngày-lễ-việt-nam)
   - [Xử lý 13 phân vùng Word phức tạp (Bảng lồng nhau, TextBoxes, SDT, Field Codes)](#xử-lý-13-phân-vùng-word-phức-tạp)
   - [Smart OCR nhận diện hợp đồng scan & ảnh chụp](#smart-ocr-nhận-diện-hợp-đồng-scan--ảnh-chụp)
   - [Tích hợp Webhook ERP / DMS doanh nghiệp](#tích-hợp-webhook-erp--dms-doanh-nghiệp)
   - [Xử lý song song đa luồng Channel & Tối ưu hóa RAM](#xử-lý-song-song-đa-luồng-channel--tối-ưu-hóa-ram)
5. [Câu hỏi thường gặp & Xử lý sự cố (FAQ)](#5-câu-hỏi-thường-gặp--xử-lý-sự-cố-faq)

---

## 1. GIỚI THIỆU TỔNG QUAN

**Antigravity Contract Date Replacer** là ứng dụng chuyên dụng chạy trên Windows Desktop, được thiết kế để giải quyết bài toán thay thế ngày tháng hàng loạt trên các tài liệu hợp đồng Microsoft Word (`.docx`).

Khác với tính năng *Find & Replace* thông thường dễ làm hỏng định dạng hoặc thay thế nhầm lẫn ngày sinh/ngày cấp CCCD của người đại diện, **Contract Date Replacer** sở hữu:
- **Thuật toán quét phân tách Run (Run-Splitting Algorithm)**: Bảo toàn 100% định dạng nguyên bản (Font, cỡ chữ, in đậm, nghiêng, màu sắc, gạch chân).
- **Trí tuệ phân loại ngữ cảnh pháp lý (Date Classification Engine)**: Tự động phân biệt Ngày ký, Ngày hiệu lực, Ngày bàn giao, Ngày thanh toán, Ngày sinh, Ngày cấp giấy tờ tùy thân.
- **Bảo vệ an toàn tuyệt đối**: Tự động sao lưu trước khi can thiệp, không bao giờ ghi đè trực tiếp lên file gốc nếu người dùng chưa xác nhận.

---

## 2. YÊU CẦU HỆ THỐNG

- **Hệ điều hành**: Windows 10 (bản 1903 trở lên) hoặc Windows 11 (64-bit).
- **Nền tảng**: .NET 8.0 Desktop Runtime.
- **Bộ nhớ RAM**: Tối thiểu 4 GB (Khuyến nghị 8 GB trở lên khi xử lý đồng thời hàng trăm hợp đồng).
- **Dung lượng ổ cứng**: Trống tối thiểu 100 MB.
- **Định dạng tập tin được hỗ trợ**: Microsoft Word OpenXML (`.docx`).

---

## 3. QUY TRÌNH SỬ DỤNG CƠ BẢN (A ĐẾN Z)

### Bước 1: Chọn thư mục hoặc tập tin hợp đồng Word
1. Tại màn hình chính (**Dashboard**), kéo thả một hoặc nhiều file `.docx` vào vùng nhận diện.
2. Hoặc bấm nút **[ 📂 Thêm Tập Tin ]** hoặc **[ 📁 Thêm Thư Mục ]** để nạp danh sách hợp đồng cần xử lý.
3. Ứng dụng sẽ hiển thị danh sách tập tin kèm dung lượng, đường dẫn và trạng thái kiểm tra ban đầu.

### Bước 2: Quét phân tích & Phân loại ngày tự động
1. Bấm nút **[ 🔍 Bắt Đầu Quét Toàn Bộ ]**.
2. Hệ thống sẽ kích hoạt Engine phân tích song song đa luồng để rà soát toàn bộ các phân vùng tài liệu.
3. Mỗi ngày phát hiện được tự động phân loại theo 12 nhóm điều khoản hợp đồng.

### Bước 3: Kiểm tra & Tùy chỉnh danh sách ngày (Review)
1. Chuyển sang màn hình **Review & Điều Chỉnh Ngày**.
2. Danh sách hiển thị toàn bộ các điểm xuất hiện ngày tháng kèm ngữ cảnh xung quanh.
3. Các công cụ hỗ trợ nhanh:
   - **Quick Fill (Điền nhanh)**: Chọn ngày mới cho toàn bộ các mục cùng loại (ví dụ: đổi toàn bộ Ngày Ký sang ngày hôm nay chỉ với 1 click).
   - **Bộ lọc thông minh**: Lọc theo từng loại ngày (Ngày ký, Ngày thanh toán...) hoặc theo file.
   - **Cảnh báo ngày nghỉ**: Tự động đánh dấu nếu ngày dự kiến rơi vào Thứ Bảy, Chủ Nhật hoặc ngày lễ Việt Nam (kèm nút áp dụng ngày làm việc tiếp theo).

### Bước 4: Áp dụng Mẫu hợp đồng (Presets / Templates)
1. Bấm nút **[ 📑 Mẫu Thiết Lập Sẵn ]** trên thanh tiêu đề.
2. Chọn một trong các mẫu chuẩn:
   - *Hợp đồng Mua bán hàng hóa*
   - *Hợp đồng Thuê nhà / Mặt bằng*
   - *Hợp đồng Lao động định hạn*
   - *Hợp đồng Dịch vụ & Phụ lục*
3. Toàn bộ logic dịch chuyển ngày (+30 ngày, +1 năm, giữ nguyên ngày sinh) sẽ được áp dụng tự động.

### Bước 5: So sánh trực quan trước khi thay thế (Preview Diff)
1. Bấm nút **[ 👁 Xem Trước ]**.
2. Màn hình so sánh Side-by-Side hiển thị nguyên văn bản gốc bên trái và văn bản sau khi thay thế bên phải.
3. Các từ được thay đổi được highlight màu sắc rõ ràng (Đỏ: nội dung cũ, Xanh: nội dung mới).

### Bước 6: Thực thi thay thế an toàn 100%
1. Bấm **[ ⚡ Thực Thi Thay Thế ]**.
2. Hệ thống tự động tạo bản sao lưu an toàn (`.backup`) trong thư mục hệ thống.
3. Quá trình thay thế diễn ra từ phải sang trái (Right-to-Left replacement) để đảm bảo không làm lệch vị trí ký tự.
4. Kiểm tra tính toàn vẹn OpenXML Schema trước khi ghi file hoàn tất.

### Bước 7: Xuất báo cáo nghiệm thu đa định dạng
1. Tại màn hình **Báo Cáo (Report)**, chọn một trong các tùy chọn xuất:
   - **[ 📈 Xuất Excel (XLSX) ]**: Tạo file Excel chuẩn gồm 3 trang tính (Tổng quan, Chi tiết vị trí ngày, Danh sách file).
   - **[ 📊 Xuất CSV ]**: Xuất file CSV định dạng UTF-8 BOM hiển thị chuẩn trong Excel.
   - **[ 📋 Xuất JSON ]**: Xuất dữ liệu máy đọc được có chữ ký băm kiểm toán.
   - **[ 🖨 Báo Cáo PDF/HTML ]**: Xuất bản in nghiệm thu có chứng thực số in ấn đẹp mắt.

---

## 4. CÁC TÍNH NĂNG NÂNG CAO & CHUYÊN SÂU

### Khôi phục dữ liệu tức thì (Backup & Rollback)
- Truy cập menu **[ 🕒 Lịch Sử & Rollback ]**.
- Mỗi đợt thay thế tạo ra một điểm khôi phục Snapshot.
- Khi cần hoàn tác, bấm **[ ↩ Khôi Phục Toàn Bộ ]** để đưa toàn bộ tập tin về trạng thái gốc tức thì.

### Nhật ký kiểm toán chống can thiệp (Audit Log & SHA-256)
- Truy cập menu **[ 🛡 Kiểm Toán ]**.
- Ghi nhận chi tiết: Người thực hiện, thời gian, tên file, vị trí, ngày cũ, ngày mới, mã băm toàn vẹn SHA-256.
- Có nút **[ 🔍 Kiểm Tra Toàn Vẹn CSDL ]** để phát hiện bất kỳ sự can thiệp trái phép nào vào lịch sử.

### Trí thông minh ngày: Âm - Dương lịch & Ngày lễ Việt Nam
- Tích hợp thuật toán thiên văn học thuần C# Hồ Ngọc Đức (múi giờ GMT+7).
- Chuyển đổi chính xác các mốc nghỉ lễ truyền thống: Giỗ Tổ Hùng Vương (10/03 Âm lịch), Tết Nguyên Đán (7 ngày nghỉ).
- Tự động phát hiện ngày làm việc (Business Days), loại trừ Thứ 7, Chủ Nhật và ngày nghỉ bù theo Bộ luật Lao động 2019.

### Xử lý 13 phân vùng Word phức tạp
Hệ thống không chỉ quét nội dung chính của văn bản mà hỗ trợ toàn diện:
1. Nội dung chính (Main Body)
2. Đầu trang (Headers - Trang đầu, trang chẵn, trang lẻ)
3. Chân trang (Footers)
4. Bảng biểu lồng nhau (Nested Tables nhiều cấp) & Ô gộp (Merged Cells)
5. Hộp văn bản (TextBoxes dạng VML và DrawingML)
6. Thẻ nội dung Structured Document Tag (SDT) — Tự động cập nhật thuộc tính `w:fullDate` của DatePicker!
7. Chú thích cuối trang (Footnotes)
8. Chú thích cuối văn bản (Endnotes)
9. Bình luận & Phản hồi (Comments)
10. Hình vẽ & SmartArt (Shapes / WordArt)
11. Hình vẽ trong Header/Footer
12. Trường tự động (Field Codes: `DATE`, `CREATEDATE`) — Tự động unlink thành văn bản tĩnh để Word không tự ý tính lại ngày!
13. Thuộc tính phân đoạn & Watermark (Section Properties)

### Smart OCR nhận diện hợp đồng scan & ảnh chụp
- Tự động trích xuất tất cả ảnh nhúng (`ImagePart`) trong tài liệu Word.
- Quét OCR tiếng Việt có dấu, trích xuất tọa độ Bounding Box và độ tin cậy Confidence.
- Phát cảnh báo an toàn rõ ràng khi phát hiện ngày tháng nằm trong ảnh bitmap không thể chỉnh sửa trực tiếp.

### Tích hợp Webhook ERP / DMS doanh nghiệp
- Cấu hình gửi thông báo tự động đến hệ thống máy chủ của doanh nghiệp khi đợt thay thế hoàn thành.
- Đi kèm chữ ký số bảo mật `X-Signature: sha256={hash}` theo thuật toán HMAC-SHA256.
- Tự động thử lại (Exponential Backoff) khi mạng chập chờn.

### Xử lý song song đa luồng Channel & Tối ưu hóa RAM
- Ứng dụng kiến trúc `System.Threading.Channels.Channel<T>` và điều tiết tải CPU thông minh.
- Cơ chế dọn dẹp bộ nhớ tự động (Gentle LOH Compaction) cho phép xử lý liên tục hàng trăm file Word dung lượng lớn mà không gây tràn RAM hay giật lag giao diện.

---

## 5. CÂU HỎI THƯỜNG GẶP & XỬ LÝ SỰ CỐ (FAQ)

**H: File gốc của tôi có bị mất hoặc hỏng không?**
> **Đ:** Tuyệt đối không. Hệ thống áp dụng cơ chế *Non-destructive Processing*: Luôn sao lưu vào thư mục dự phòng và ghi kết quả ra file mới. File gốc hoàn toàn không bị can thiệp.

**H: Nếu ngày trong hợp đồng là "ngày 02 tháng 09 năm 2024", phần mềm có đổi thành "ngày 02 tháng 09 năm 2026" được không?**
> **Đ:** Hoàn toàn được. Bộ engine nhận diện ngữ cảnh tiếng Việt hỗ trợ định dạng chữ đầy đủ ("ngày... tháng... năm..."), định dạng gạch chéo (dd/MM/yyyy), gạch ngang (dd-MM-yyyy) và chuẩn ISO (yyyy-MM-dd).

**H: Làm thế nào để không thay đổi ngày sinh của khách hàng trong hợp đồng?**
> **Đ:** Hệ thống tự động phân loại mục đó là `BirthDate` và mặc định bỏ chọn (không thay thế). Bạn cũng có thể dùng quy tắc loại trừ trong màn hình Cài đặt hoặc Review.

**H: Word báo lỗi "The file is corrupted" sau khi thay thế ngày bằng các công cụ khác, phần mềm này có bị không?**
> **Đ:** Không. Phần mềm tích hợp `WordOpenXmlValidator` kiểm tra nghiêm ngặt theo chuẩn ECMA-376 OpenXML quốc tế trước khi lưu file, đảm bảo tài liệu mở lên 100% mượt mà trong Microsoft Word.

---
*Bản quyền © 2026 Antigravity IDE Team. Tài liệu được phát hành kèm bản Release Candidate v1.0.0.*

## Cập nhật Word + Excel và giao diện (22/10/2026)

- Nhập file .docx và .xlsx trong cùng đợt: chọn file, thư mục hoặc kéo thả.
- Excel nhận diện ngày trong chữ và ngày lưu dạng số dựa trên định dạng ô; không coi mã số thông thường là ngày.
- Hỗ trợ chuỗi dùng chung, văn bản có nhiều kiểu chữ, hệ ngày 1900/1904 và giữ phần giờ khi đổi ngày dạng số.
- Công thức và ô bị khóa trên sheet được bảo vệ được hiển thị để rà soát nhưng không cho thay trực tiếp.
- Có tùy chọn quét sheet ẩn và lọc kết quả theo sheet. Sau khi bật quét sheet ẩn, quét lại để nạp thêm nội dung.
- Giao diện chia khung nhập tài liệu, bộ lọc, danh sách/chi tiết và điều hướng bằng border; có thanh bước và mật độ Gọn/Thoải mái.
- Chọn ngày Excel để xem lưới các ô lân cận; ô đang xem được đánh dấu và tô màu.
- Thao tác chọn và áp dụng hàng loạt chỉ tác động các dòng đang hiện theo bộ lọc.
- Kết quả luôn là file mới, giữ phần mở rộng .docx hoặc .xlsx, có sao lưu và xác minh.
- Chạy bản mới bằng Chay_App.bat hoặc Publish_App_Excel/ContractDateReplacer.App.exe. Bản này dùng .NET Desktop Runtime 8 đã có trên máy.
- Chưa hỗ trợ .xls nhị phân; hãy lưu thành .xlsx trong Excel trước khi nhập.

## CẬP NHẬT: NGÀY THIẾU NĂM VÀ BẢNG CHẤM CÔNG

- Ngày dạng `09/10`, `9/10`, `29/02` được nhận diện trong Word và văn bản Excel. Màn hình rà soát hiển thị **Thiếu năm**; chọn ngày mới để thay, kết quả vẫn giữ dạng không có năm và số chữ số như bản gốc.
- Menu và danh sách xổ xuống sử dụng nền sáng đặc.
- Mở **Công cụ → Đổi tháng bảng chấm công**. Chọn file `.xlsx`, chọn bảng được nhận diện, chọn tháng và năm, bấm **Xem trước** rồi **Xuất bản sao Excel**.
- Công được giữ theo số ngày. Hàng ngày/thứ và dòng thời gian được cập nhật; Chủ nhật được tô xanh từ hàng ngày đến hàng tổng. Ngày dư trong tháng ngắn được để trống. Nếu ngày dư có dữ liệu hoặc công thức, ứng dụng báo địa chỉ ô và chặn xuất; hãy xử lý trong bản sao nguồn phù hợp rồi chọn lại file.
- Công cụ hỗ trợ bảng có 28–31 cột ngày, hàng thứ bên dưới, STT, Họ và tên, cột Tổng công hoặc Tổng cộng và dòng tháng/năm; không bắt buộc hàng tổng cuối bảng. Khi cần, ứng dụng bổ sung cột ngày còn thiếu trước cột tổng và dời tổng/ghi chú sang phải trong vùng bảng; giữ nguyên phần chữ ký. File đã xuất tháng ngắn có thể tiếp tục dùng để đổi tháng sau.
- File gốc không bị ghi đè. Lịch sử sao lưu được lưu trước khi xuất; nếu sao lưu thất bại, ứng dụng dừng xuất. Nếu nguồn thay đổi sau khi xem trước, cần tạo lại bản xem trước.
- Dùng **Quay lại** để về trang chính; **Mở thư mục** để chọn file kết quả sau khi xuất thành công.

### Đồng bộ phần Thời gian

Khi chọn tháng/năm và xuất bảng chấm công, ứng dụng cập nhật cả phần **Tháng** và **Từ ngày … đến ngày …** trong tiêu đề bảng. Ngày bắt đầu là ngày 1; ngày kết thúc là ngày cuối tháng, có tính năm nhuận. Hỗ trợ nội dung cùng ô, ô Từ/Đến riêng, nhãn và giá trị ngày ở các ô liền kề; giữ cách viết nhãn và dạng ngày ban đầu. Ngày ở phần ghi chú ngoài tiêu đề bảng không bị thay theo thao tác này.

### Tính toán lại sau khi xuất

Bản xuất xóa bộ nhớ đệm thứ tự tính toán cũ (`calcChain`) và yêu cầu Excel tính lại để tránh cảnh báo phục hồi khi cột tổng được dời. Cài đặt tính toán khác của workbook được giữ nguyên. Nếu workbook đang dùng chế độ tính toán thủ công, dùng F9 trong Excel khi cần tính lại.
