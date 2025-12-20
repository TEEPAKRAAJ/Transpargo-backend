using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Razorpay.Api;
using System;
using System.Globalization;
using System.Reflection.Emit;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Transpargo.Models;
using Transpargo.Services;


namespace Transpargo.Controllers
{
    public class ShipmentRow
    {
        public int s_id { get; set; }
        public DateTime created_at { get; set; }
        public string? status { get; set; }
        public string? duty_mode { get; set; }
        public long? shipping_cost { get; set; }
        public int id { get; set; }  // user id
    }

    public class SenderRow
    {
        public int sender_id { get; set; }
        public int s_id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? postal { get; set; }
        public string? Country { get; set; }
    }

    public class ReceiverRow
    {
        public int receiver_id { get; set; }
        public int s_id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? postal { get; set; }
        public string? Country { get; set; }
    }
    public class SenderLogItem
    {
        public string? title { get; set; }
        public string date { get; set; }
        public string? time { get; set; }
        public string? icon { get; set; }
    }


    [ApiController]
    [Route("api/payment")]
    [Authorize]
    public class PaymentController : ControllerBase
    {
        //helpers
        private static string SafeString(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString() ?? "",
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.Null => "",
                _ => ""
            };
        }

        private static long SafeLong(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetInt64(),
                JsonValueKind.String when long.TryParse(el.GetString(), out var v) => v,
                _ => 0
            };
        }
        //helpers




        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly IConfiguration _config;
        private readonly TradeComplianceService _trade;   // kept for future, not used now
        private readonly ShippingCostService _ship;

        public PaymentController(IConfiguration config, TradeComplianceService trade, ShippingCostService ship)
        {
            _config = config;
            _baseUrl = config["SUPABASE_URL"] + "/rest/v1/";
            _apiKey = config["SUPABASE_KEY"] ?? throw new InvalidOperationException("SUPABASE_KEY missing");

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("apikey", _apiKey);
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            _trade = trade;
            _ship = ship;
        }

        private LogEntry CreateLog(string title, bool action, string link, string label, string status)
        {
            return new LogEntry
            {
                date = null,
                time = null,
                icon = status,
                title = title,
                action = action,
                actionLabel = label,
                action_href = link
            };
        }

        // --------------------------------------------
        // GET SHIPMENT FOR PAYMENT
        // --------------------------------------------
        [HttpGet("shipment/{shipmentId:int}")]
        public async Task<IActionResult> GetShipmentForPayment(int shipmentId)
        {
            try
            {
                Console.WriteLine("➡️ ENTERED GetShipmentForPayment");
                Console.WriteLine($"📦 Shipment ID received: {shipmentId}");

                var userId = User.FindFirst("user_id")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                Console.WriteLine($"👤 User ID: {userId}");

                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User not authenticated");

                string query =
                    $"{_baseUrl}Shipment?"
                    + $"s_id=eq.{shipmentId}&id=eq.{userId}"
                    + "&select=s_id,status,created_at,duty_mode,shipping_cost,"
                    + "Sender:Sender(*),Receiver:Receiver(*),Product:Product(*)";

                Console.WriteLine($"📡 Fetching: {query}");

                var response = await _http.GetAsync(query);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, json);

                var rows = JsonSerializer.Deserialize<List<JsonElement>>(json);

                if (rows == null || rows.Count == 0)
                    return NotFound("Shipment not found");

                var record = rows[0];
                var sender = record.GetProperty("Sender")[0];
                var receiver = record.GetProperty("Receiver")[0];

                return Ok(new
                {
                    success = true,
                    shipment = new
                    {
                        s_id = record.GetProperty("s_id").GetInt32(),
                        status = record.GetProperty("status").GetString(),

                        shipping_cost = record.TryGetProperty("shipping_cost", out var sc)
                                            ? SafeLong(sc)
                                            : 0,

                        sender = new
                        {
                            name = SafeString(sender.GetProperty("Name")),
                            email = SafeString(sender.GetProperty("Email")),
                            phone = SafeString(sender.GetProperty("Phone")),
                            address = SafeString(sender.GetProperty("Address")),
                            city = SafeString(sender.GetProperty("City")),
                            state = SafeString(sender.GetProperty("State")),
                            postal = sender.TryGetProperty("Postal", out var sp) ? SafeString(sp) : "",
                            country = SafeString(sender.GetProperty("Country"))
                        },

                        receiver = new
                        {
                            name = SafeString(receiver.GetProperty("Name")),
                            email = SafeString(receiver.GetProperty("Email")),
                            phone = SafeString(receiver.GetProperty("Phone")),
                            address = SafeString(receiver.GetProperty("Address")),
                            city = SafeString(receiver.GetProperty("City")),
                            state = SafeString(receiver.GetProperty("State")),
                            postal = receiver.TryGetProperty("postal", out var rp) ? SafeString(rp) : "",
                            country = SafeString(receiver.GetProperty("Country"))
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("🔥 GetShipmentForPayment crashed");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);

                return StatusCode(500, new { error = "Backend crashed", details = ex.Message });
            }
        }

        // --------------------------------------------
        // CALCULATE SHIPPING ONLY (no duty, no GST)
        // --------------------------------------------
        [HttpGet("calculate/{shipmentId:int}")]
        public async Task<IActionResult> CalculateCharges(int shipmentId)
        {
            try
            {
                // USER AUTH
                var userId = User.FindFirst("user_id")?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User not authenticated");

                // FETCH SHIPMENT RECORD
                string query =
                    $"{_baseUrl}Shipment?"
                    + $"s_id=eq.{shipmentId}&id=eq.{userId}"
                    + "&select=s_id,status,created_at,duty_mode,shipping_cost,"
                    + "Sender_log,Receiver_log,Sender:Sender(*),Receiver:Receiver(*),Product:Product(*)";

                var response = await _http.GetAsync(query);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, json);

                var rows = JsonSerializer.Deserialize<List<JsonElement>>(json);
                if (rows == null || rows.Count == 0)
                    return NotFound($"No shipment found for s_id={shipmentId} and user={userId}");

                var record = rows[0];

                // PRODUCT + RECEIVER - with null checks
                var productArray = record.GetProperty("Product");
                var receiverArray = record.GetProperty("Receiver");

                if (productArray.GetArrayLength() == 0 || receiverArray.GetArrayLength() == 0)
                    return BadRequest("Missing product or receiver information");

                var product = productArray[0];
                var receiver = receiverArray[0];

                // REQUIRED CALCULATIONS
                string dest = receiver.TryGetProperty("Country", out var c1)
                    ? (c1.GetString() ?? "")
                    : (receiver.GetProperty("country").GetString() ?? "");

                decimal weight =
                    product.TryGetProperty("weight", out var w1) ? w1.GetDecimal() :
                    product.TryGetProperty("shipment_weight", out var w2) ? w2.GetDecimal() : 0;

                decimal qty =
                    product.TryGetProperty("no_of_packages", out var q1) ? q1.GetDecimal() :
                    product.TryGetProperty("packages", out var q2) ? q2.GetDecimal() : 1;

                decimal length =
                    product.TryGetProperty("length", out var l1) ? l1.GetDecimal() :
                    product.TryGetProperty("dimensions_length", out var l2) ? l2.GetDecimal() : 10;

                decimal width =
                    product.TryGetProperty("width", out var wl1) ? wl1.GetDecimal() :
                    product.TryGetProperty("dimensions_width", out var wl2) ? wl2.GetDecimal() : 10;

                decimal height =
                    product.TryGetProperty("height", out var h1) ? h1.GetDecimal() :
                    product.TryGetProperty("dimensions_height", out var h2) ? h2.GetDecimal() : 10;

                string unit = product.TryGetProperty("unit", out var u1) ? (u1.GetString() ?? "cm") : "cm";

                // SHIPPING CALCULATION
                decimal shipping = _ship.Calculate(new ShippingInput
                {
                    Country = dest,
                    Weight = weight,
                    Qty = (int)qty,
                    L = length,
                    W = width,
                    H = height,
                    Unit = unit
                });

                double roundedShipping = (int)Math.Round(shipping);

                //fine calculation

                double fineAmount = 0;
                int daysPassed = 0;
                if (record.TryGetProperty("Sender_log", out var senderLogJson))
                {
                    Console.WriteLine("Sender_log JSON: " + senderLogJson.GetRawText());
                }
                else
                {
                    Console.WriteLine("Sender_log not found in record");
                }


                // Get Sender_log and find "Document Upload"
                try
                {
                    var senderLog = JsonSerializer.Deserialize<List<SenderLogItem>>(senderLogJson.GetRawText());
                    var docUpload = senderLog?.FirstOrDefault(s => s.title == "Document Upload");
                    //debug
                    if (docUpload == null)
                        Console.WriteLine("Document Upload not found in log");

                    if (docUpload != null)
                    {
                        //debug
                        Console.WriteLine(docUpload);
                        Console.WriteLine("The crurent date:");
                        Console.WriteLine(DateTime.Now);
                        Console.WriteLine("got date");

                        //parse string to datetime

                        // parse string to datetime (date + time)
                        if (!string.IsNullOrWhiteSpace(docUpload?.date))
                        {
                            string datePart = docUpload.date;
                            string timePart = string.IsNullOrWhiteSpace(docUpload.time)
                                ? "12:00 AM"
                                : docUpload.time;

                            string combined = $"{datePart} {timePart}";

                            if (DateTime.TryParseExact(
                                combined,
                                new[] { "yyyy-MM-dd hh:mm tt", "yyyy-MM-dd HH:mm", "yyyy-MM-dd" },
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out DateTime uploadDate))
                            {
                                Console.WriteLine($"✅ Parsed upload date: {uploadDate}");
                                daysPassed = (DateTime.Now - uploadDate).Days;
                                Console.WriteLine("Days passed: " + daysPassed);
                            }
                            else
                            {
                                Console.WriteLine($"❌ Failed to parse combined date: {combined}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ Document Upload date is null or empty");
                        }



                        //If > 90 days: Send alert→ didnt delete from database yet
                        if (daysPassed > 90)
                        {
                            return Ok(new { message = "Shipment cancelled due to late payment (>90 days)" });
                        }

                        // If > 5 days: Calculate fine
                        //LateFine = ₹75 + (0.5% × DutyAmount × DaysLate)

                        if (daysPassed > 5)
                        {
                            fineAmount = 75 + ((daysPassed - 5) * (0.005) * (roundedShipping));
                        }
                    }
                }
                catch (JsonException)
                {
                    // If log parsing fails, continue without fine


                }


                // Update shipping cost + fine in database
                var updateBody = new
                {
                    shipping_cost = roundedShipping + fineAmount
                };

                var updateJson = JsonSerializer.Serialize(updateBody);
                var content = new StringContent(updateJson, Encoding.UTF8, "application/json");
                await _http.PatchAsync($"{_baseUrl}Shipment?s_id=eq.{shipmentId}", content);

                // FINAL RETURN
                return Ok(new
                {
                    shipping = roundedShipping + fineAmount,
                    fine = fineAmount,
                    days = daysPassed
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in CalculateCharges: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }


        // --------------------------------------------
        // CREATE RAZORPAY ORDER (for shipping amount)
        // --------------------------------------------
        [HttpPost("create-order")]
        public IActionResult CreateOrder([FromBody] CreateOrderRequest req)
        {
            Console.WriteLine($"💰 Creating Razorpay Order for Shipment {req.ShipmentId} | Amount: {req.AmountInPaise}");

            var client = new RazorpayClient(_config["Razorpay:KeyId"], _config["Razorpay:KeySecret"]);
            var order = client.Order.Create(new Dictionary<string, object>
            {
                ["amount"] = req.AmountInPaise,
                ["currency"] = "INR",
                ["receipt"] = req.ShipmentId,
                ["payment_capture"] = 1
            });

            return Ok(new
            {
                success = true,
                orderId = order["id"].ToString(),
                keyId = _config["Razorpay:KeyId"],
                amount = req.AmountInPaise
            });
        }

        // --------------------------------------------
        // VERIFY PAYMENT SIGNATURE
        // --------------------------------------------
        [HttpPost("verify")]
        public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest req)
        {
            Console.WriteLine($"🔐 Verifying Payment for Shipment {req.ShipmentId}");

            var secret = _config["Razorpay:KeySecret"]!;
            var expected = GenerateHmac($"{req.razorpay_order_id}|{req.razorpay_payment_id}", secret);

            if (!string.Equals(expected, req.razorpay_signature, StringComparison.OrdinalIgnoreCase))
                return BadRequest("⚠ Invalid payment signature");

            // Update DB status to Paid
            var updateBody = new { status = "Payment Successful" };
            var content = new StringContent(JsonSerializer.Serialize(updateBody), Encoding.UTF8, "application/json");

            var result = await _http.PatchAsync($"{_baseUrl}Shipment?s_id=eq.{req.ShipmentId}", content);
            Console.WriteLine("📌 DB Update Response → " + await result.Content.ReadAsStringAsync());

            return Ok(new { success = true, message = "Payment Verified & Shipment Updated" });
        }

        private static string GenerateHmac(string input, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)))
                 .Replace("-", "").ToLowerInvariant();
        }

        // --------------------------------------------
        // UPDATE PAYMENT LOG AFTER SUCCESS
        // --------------------------------------------
        [HttpPut("{id}/updatepaymentlog")]
        public async Task<IActionResult> UpdateDocLog(int id)
        {
            try
            {
                var shipmentResponse = await _http.GetAsync($"{_baseUrl}Shipment?s_id=eq.{id}&select=*");

                if (!shipmentResponse.IsSuccessStatusCode)
                    return NotFound("Shipment not found");

                var shipmentJson = await shipmentResponse.Content.ReadAsStringAsync();
                var shipments = JsonSerializer.Deserialize<List<JsonElement>>(shipmentJson);

                if (shipments == null || shipments.Count == 0)
                    return NotFound("Shipment not found");

                var shipmentElement = shipments[0];
                var senderLog = shipmentElement.GetProperty("Sender_log").Deserialize<List<LogEntry>>() ?? new List<LogEntry>();
                var receiverLog = shipmentElement.GetProperty("Receiver_log").Deserialize<List<LogEntry>>() ?? new List<LogEntry>();

                if (senderLog.Any())
                {
                    var sLogLast = senderLog.Last();
                    sLogLast.icon = "success";
                    sLogLast.action = false;
                    sLogLast.date = DateTime.Now.ToString("yyyy-MM-dd");
                    sLogLast.time = DateTime.Now.ToString("hh:mm tt");
                }

                if (receiverLog.Any())
                {
                    var rLogLast = receiverLog.Last();
                    rLogLast.icon = "success";
                    rLogLast.action = false;
                    rLogLast.date = DateTime.Now.ToString("yyyy-MM-dd");
                    rLogLast.time = DateTime.Now.ToString("hh:mm tt");
                }

                var sLog = CreateLog("Customs Export", false, null, null, "pending");
                var rLog = CreateLog("Customs Export", false, null, null, "pending");

                senderLog.Add(sLog);
                receiverLog.Add(rLog);

                var updateBody = new
                {
                    Sender_log = senderLog,
                    Receiver_log = receiverLog
                };

                var updateJson = JsonSerializer.Serialize(updateBody);
                var content = new StringContent(updateJson, Encoding.UTF8, "application/json");
                var updateResponse = await _http.PatchAsync($"{_baseUrl}Shipment?s_id=eq.{id}", content);

                updateResponse.EnsureSuccessStatusCode();

                return Ok(new { success = true, message = "Payment log updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPut("{id}/updatedutylog")]
        public async Task<IActionResult> UpdatedutyLog(int id)
        {
            try
            {
                var shipmentResponse = await _http.GetAsync($"{_baseUrl}Shipment?s_id=eq.{id}&select=*");

                if (!shipmentResponse.IsSuccessStatusCode)
                    return NotFound("Shipment not found");

                var shipmentJson = await shipmentResponse.Content.ReadAsStringAsync();
                var shipments = JsonSerializer.Deserialize<List<JsonElement>>(shipmentJson);

                if (shipments == null || shipments.Count == 0)
                    return NotFound("Shipment not found");

                var shipmentElement = shipments[0];
                var senderLog = shipmentElement.GetProperty("Sender_log").Deserialize<List<LogEntry>>() ?? new List<LogEntry>();
                var receiverLog = shipmentElement.GetProperty("Receiver_log").Deserialize<List<LogEntry>>() ?? new List<LogEntry>();

                if (senderLog.Any())
                {
                    var sLogLast = senderLog.Last();
                    sLogLast.icon = "success";
                    sLogLast.action = false;
                    sLogLast.date = DateTime.Now.ToString("yyyy-MM-dd");
                    sLogLast.time = DateTime.Now.ToString("hh:mm tt");
                }

                if (receiverLog.Any())
                {
                    var rLogLast = receiverLog.Last();
                    rLogLast.icon = "success";
                    rLogLast.action = false;
                    rLogLast.date = DateTime.Now.ToString("yyyy-MM-dd");
                    rLogLast.time = DateTime.Now.ToString("hh:mm tt");
                }

                var sLog = new LogEntry
                {
                    date = DateTime.Now.ToString("yyyy-MM-dd"),
                    time = DateTime.Now.ToString("hh:mm tt"),
                    icon = "success",
                    title = "Customs Cleared",
                    action = false,
                };
                var rLog = new LogEntry
                {
                    date = DateTime.Now.ToString("yyyy-MM-dd"),
                    time = DateTime.Now.ToString("hh:mm tt"),
                    icon = "success",
                    title = "Customs Cleared",
                    action = false,
                };

                senderLog.Add(sLog);
                receiverLog.Add(rLog);

                var sloglast = CreateLog("Delivered", false, null, null, "pending");
                var rloglast = CreateLog("Delivered", false, null, null, "pending");
                senderLog.Add(sloglast);
                receiverLog.Add(rloglast);

                var updateBody = new
                {
                    Sender_log = senderLog,
                    Receiver_log = receiverLog,
                    status = "Duty Payment Successful"
                };

                var updateJson = JsonSerializer.Serialize(updateBody);
                var content = new StringContent(updateJson, Encoding.UTF8, "application/json");
                var updateResponse = await _http.PatchAsync($"{_baseUrl}Shipment?s_id=eq.{id}", content);

                updateResponse.EnsureSuccessStatusCode();

                return Ok(new { success = true, message = "Payment log updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("verify-duty")]
        public async Task<IActionResult> VerifyDutyPayment([FromBody] VerifyPaymentRequest req)
        {
            Console.WriteLine($"🔐 Verifying DUTY Payment for Shipment {req.ShipmentId}");

            var secret = _config["Razorpay:KeySecret"]!;
            var expected = GenerateHmac($"{req.razorpay_order_id}|{req.razorpay_payment_id}", secret);

            if (!string.Equals(expected, req.razorpay_signature, StringComparison.OrdinalIgnoreCase))
                return BadRequest("⚠ Invalid duty payment signature");

            
            return Ok(new { success = true, message = "Duty Payment Verified & Updated" });
        }


        [HttpGet("calculate-duty/{shipmentId:int}")]
        public async Task<IActionResult> CalculateDutyOnly(int shipmentId)
        {
            try
            {
                var userId = User.FindFirst("user_id")?.Value
                              ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userId))
                    return Unauthorized("User not authenticated");

                string query =
                    $"{_baseUrl}Shipment?"
                    + $"s_id=eq.{shipmentId}&id=eq.{userId}"
                    + "&select=Sender_log,Sender:Sender(*),Receiver:Receiver(*),Product:Product(*)";

                var response = await _http.GetAsync(query);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, json);

                var rows = JsonSerializer.Deserialize<List<JsonElement>>(json);

                if (rows == null || rows.Count == 0)
                    return NotFound("Shipment not found");

                var record = rows[0];
                var product = record.GetProperty("Product")[0];
                var receiver = record.GetProperty("Receiver")[0];

                // --- Extract ---
                string dest = receiver.GetProperty("Country").GetString() ?? "";
                string hs = product.TryGetProperty("hs_code", out var h1) ? h1.GetString() : "";

                decimal declared = product.TryGetProperty("value", out var v1) ? v1.GetDecimal() : 0;
                decimal weight = product.TryGetProperty("weight", out var w1) ? w1.GetDecimal() : 0;

                // --- Duty + GST only ---
                var (dutyPct, gstPct) = await _trade.ComputeAsync(dest, hs, declared, weight);

                decimal duty = Math.Round((declared * dutyPct / 100), 2);
                decimal gst = Math.Round(((declared + duty) * gstPct / 100), 2);

                //fine calculation

                decimal fineAmount = 0m;
                int daysPassed = 0;
                // Get Sender_log and find Arrived at Customs
                if (record.TryGetProperty("Sender_log", out var senderLogJson))
                {
                    Console.WriteLine("Sender_log JSON: " + senderLogJson.GetRawText());
                }
                else
                {
                    Console.WriteLine("Sender_log not found in record");
                }

                try
                {
                    var senderLog = JsonSerializer.Deserialize<List<SenderLogItem>>(senderLogJson.GetRawText());
                    var docUpload = senderLog?.FirstOrDefault(s => s.title == "Arrived at Customs");
                    //debug
                    if (docUpload == null)
                        Console.WriteLine("Arrived at Customs not found in log");

                    if (docUpload != null)
                    {
                        //debug
                        Console.WriteLine(docUpload);
                        Console.WriteLine("The crurent date:");
                        Console.WriteLine(DateTime.Now);
                        Console.WriteLine("got date");

                        //parse string to datetime
                        if (DateTime.TryParse(docUpload.date.ToString(), out DateTime uploadDate))
                        {
                            Console.WriteLine(uploadDate);
                            daysPassed = (DateTime.Now - uploadDate).Days;
                            Console.WriteLine("Days passed: " + daysPassed);
                        }
                        else
                        {
                            Console.WriteLine("Failed to parse Arrived at Customs date");
                        }


                        // If > 90 days: Alert
                        if (daysPassed > 90)
                        {
                            return Ok(new { message = "Shipment cancelled due to late payment (>90 days)" });
                        }

                        // If > 5 days: Calculate fine
                        if (daysPassed > 5)
                        {
                            fineAmount = 75m + ((daysPassed - 5) * (0.005m) * (duty + gst));
                        }
                    }
                }
                catch (JsonException)
                {
                    // If log parsing fails, continue without fine
                }

                decimal totalPayable = duty + gst + fineAmount;

                return Ok(new
                {
                    dutyRate = dutyPct,
                    gstRate = gstPct,
                    duty,
                    gst,
                    fineAmount,
                    totalPayable
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }


        [HttpPost("verify-charges")]
        public IActionResult VerifyChargesPayment([FromBody] VerifyPaymentRequest req)
        {
            Console.WriteLine($"🔐 Verifying CHARGES Payment for Shipment {req.ShipmentId}");

            // 1️⃣ Load secret key
            var secret = _config["Razorpay:KeySecret"]!;

            // 2️⃣ Generate expected signature
            var expected = GenerateHmac($"{req.razorpay_order_id}|{req.razorpay_payment_id}", secret);

            // 3️⃣ Compare signatures
            if (!string.Equals(expected, req.razorpay_signature, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { success = false, message = "⚠ Invalid charges payment signature" });

            // 4️⃣ Do NOT update status here (ChargesPaid endpoint will handle it)
            return Ok(new { success = true, message = "Charges Payment Verified" });
        }

        [HttpGet("{id}/log")]
        public async Task<IActionResult> GetPaymentLog(int id)
        {
            try
            {
                var response = await _http.GetAsync(
                    $"{_baseUrl}Shipment?s_id=eq.{id}&select=payment_log"
                );

                if (!response.IsSuccessStatusCode)
                    return NotFound(new { success = false, message = "Shipment not found" });

                var json = await response.Content.ReadAsStringAsync();
                var rows = JsonSerializer.Deserialize<List<JsonElement>>(json);

                if (rows == null || rows.Count == 0)
                    return NotFound(new { success = false, message = "Shipment not found" });

                var shipment = rows[0];

                if (!shipment.TryGetProperty("payment_log", out var paymentLogElement) ||
                    paymentLogElement.ValueKind != JsonValueKind.Object)
                {
                    return Ok(new
                    {
                        success = true,
                        payment_log = new Dictionary<string, decimal>()
                    });
                }

                var paymentLog = new Dictionary<string, decimal>();

                foreach (var prop in paymentLogElement.EnumerateObject())
                {
                    // ⛔ skip name field
                    if (prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                        continue;

                    decimal value;

                    // ✅ NUMBER
                    if (prop.Value.ValueKind == JsonValueKind.Number &&
                        prop.Value.TryGetDecimal(out value))
                    {
                        paymentLog[prop.Name] = value;
                    }
                    // ✅ STRING NUMBER
                    else if (prop.Value.ValueKind == JsonValueKind.String &&
                             decimal.TryParse(prop.Value.GetString(), out value))
                    {
                        paymentLog[prop.Name] = value;
                    }
                }

                return Ok(new
                {
                    success = true,
                    payment_log = paymentLog
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetPaymentLog ERROR: " + ex);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to load payment log"
                });
            }
        }



        [HttpPut("{id}/charges-paid")]
        public async Task<IActionResult> UpdateChargesPaid(int id)
        {
            try
            {
                // Check if shipment exists
                var check = await _http.GetAsync($"{_baseUrl}Shipment?s_id=eq.{id}&select=s_id");
                if (!check.IsSuccessStatusCode)
                    return NotFound(new { success = false, message = "Shipment not found" });

                // Update
                var updateBody = new { status = "Charges Paid" };
                var content = new StringContent(JsonSerializer.Serialize(updateBody), Encoding.UTF8, "application/json");

                var update = await _http.PatchAsync($"{_baseUrl}Shipment?s_id=eq.{id}", content);
                var updateJson = await update.Content.ReadAsStringAsync();

                return Ok(new
                {
                    success = true,
                    message = "Charges marked as paid"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }


    }

    public class CreateOrderRequest
    {
        public int AmountInPaise { get; set; }
        public string ShipmentId { get; set; } = "";
    }

    public class VerifyPaymentRequest
    {
        public string razorpay_order_id { get; set; } = "";
        public string razorpay_payment_id { get; set; } = "";
        public string razorpay_signature { get; set; } = "";
        public string ShipmentId { get; set; } = "";
    }
}
