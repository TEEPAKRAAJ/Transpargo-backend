using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Transpargo.DTOs;
using Transpargo.Models;
using Transpargo.Services;

namespace Transpargo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShipmentsController : ControllerBase
    {
        private readonly SupabaseServices _supabase;

        public ShipmentsController(SupabaseServices supabase)
        {
            _supabase = supabase;
            
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

        // ----------------------------------------------------
        // GET ALL SHIPMENTS
        // ----------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetShipments()
        {
            var client = await _supabase.GetClientAsync();

            var shipmentDb = await client.From<Shipment>().Get();
            var senderDb = await client.From<Sender>().Get();
            var receiverDb = await client.From<Receiver>().Get();
            var productDb = await client.From<Product>().Get();

            var shipments = shipmentDb.Models;
            var senders = senderDb.Models;
            var receivers = receiverDb.Models;
            var products = productDb.Models;

            var list = new List<ShipmentDashboardDto>();

            foreach (var s in shipments)
            {
                var sender = senders.FirstOrDefault(x => x.ShipmentId == s.SId);
                var receiver = receivers.FirstOrDefault(x => x.ShipmentId == s.SId);
                var product = products.FirstOrDefault(x => x.ShipmentId == s.SId);
                if (sender == null || receiver == null || product == null)
                    continue;

                var dto = new ShipmentDashboardDto
                {
                    Id = "SHP" + s.SId.ToString(),
                    Sender = sender.Name ?? "Unknown",
                    Receiver = receiver.Name ?? "Unknown",
                    Value = product.Value,
                    Status = s.Status ?? "Pending",
                    Hs = product.HsCode ?? "",
                    HsApproved = (s.Status ?? "").Trim().ToLower() == "hs approved",

                    Origin = $"{sender.City ?? "Unknown"}, {sender.Country ?? "Unknown"}",
                    Destination = $"{receiver.City ?? "Unknown"}, {receiver.Country ?? "Unknown"}",

                    SenderEmail = sender.Email,        
                    ReceiverEmail = receiver.Email,    

                    DeclaredValue = product.Value.HasValue ? $"₹{product.Value.Value:N0}" : "₹0",
                    Description = product.Description ?? "",
                    Category = product.Category ?? "",
                    Type = product.Type ?? "",
                    Shippingcost = s.ShippingCost.HasValue ? $"₹{s.ShippingCost.Value:N0}" : "₹0",
                    PaymentStatus = "Pending",
                    Reason = s.Reason,

                    Sender_log = s.Sender_Log,
                    Receiver_log = s.Receiver_Log,

                    created_at = s.created_at,
                    SenderHs = product.SenderHs ?? ""
                };




                list.Add(dto);
            }

            return Ok(list);
        }

        [HttpPost("{id}/initiate-payment")]
        public async Task<IActionResult> InitiatePayment(int id, [FromBody] Dictionary<string, object> paymentData)
        {
            var client = await _supabase.GetClientAsync();

            // Fetch shipment
            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound(new { message = "Shipment not found" });

            // -------- CLEAN JSON ELEMENTS --------
            var cleanDict = new Dictionary<string, object>();

            foreach (var kv in paymentData)
            {
                if (kv.Value is JsonElement elem)
                {
                    switch (elem.ValueKind)
                    {
                        case JsonValueKind.Number:
                            cleanDict[kv.Key] = elem.TryGetInt64(out long iVal) ? iVal : elem.GetDouble();
                            break;

                        case JsonValueKind.String:
                            cleanDict[kv.Key] = elem.GetString();
                            break;

                        default:
                            cleanDict[kv.Key] = elem.ToString();
                            break;
                    }
                }
                else
                {
                    cleanDict[kv.Key] = kv.Value;
                }
            }

            // -------- UPDATE LAST LOG --------
            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            void UpdateLastLogSender(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                if (last.title == "Requested Return" || last.title == "Requested Destruction")
                {
                    last.date = DateTime.Now.ToString("yyyy-MM-dd");
                    last.time = DateTime.Now.ToString("hh:mm tt");
                    last.action = true;
                    last.actionLabel = "Pay Charges";
                    last.action_href = "/user/charges/sender/" + id;
                }
            }
            void UpdateLastLogReceiver(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                if (last.title == "Requested Return" || last.title == "Requested Destruction")
                {
                    last.date = DateTime.Now.ToString("yyyy-MM-dd");
                    last.time = DateTime.Now.ToString("hh:mm tt");
                    last.action = false; // disable button
                }
            }

            UpdateLastLogSender(shipment.Sender_Log);
            UpdateLastLogReceiver(shipment.Receiver_Log);

            // -------- SAVE CHANGES --------
            await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.payment_log, cleanDict)
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "Payment log saved and logs updated.",
                payment_log = cleanDict
            });
        }
        [HttpPut("{id}/status-returned")]
        public async Task<IActionResult> MarkReturned(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            // -------- UPDATE LAST LOG --------
            void UpdateLast(List<LogEntry> logs)
            {
                if (logs.Count == 0) return;

                var last = logs.Last();
                last.date = DateTime.Now.ToString("yyyy-MM-dd");
                last.time = DateTime.Now.ToString("hh:mm tt");
                last.action = false;
                last.icon = "success";
            }

            UpdateLast(shipment.Sender_Log);
            UpdateLast(shipment.Receiver_Log);

            // -------- ADD NEW LOG ENTRY (Returned Shipment) --------
            var newSenderLog = new LogEntry
            {
                title = "Returned Shipment",
                icon = "success",
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                time = DateTime.Now.ToString("hh:mm tt"),
                action = false
            };

            var newReceiverLog = new LogEntry
            {
                title = "Returned Shipment",
                icon = "success",
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                time = DateTime.Now.ToString("hh:mm tt"),
                action = false
            };

            shipment.Sender_Log.Add(newSenderLog);
            shipment.Receiver_Log.Add(newReceiverLog);

            // -------- UPDATE STATUS --------
            shipment.Status = "Returned";

            await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, shipment.Status)
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new { success = true, message = "Shipment marked as Returned." });
        }

        [HttpPut("{id}/status-abort")]
        public async Task<IActionResult> MarkAbort(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            // -------- UPDATE LAST LOG --------
            void UpdateLast(List<LogEntry> logs)
            {
                if (logs.Count == 0) return;

                var last = logs.Last();
                last.date = DateTime.Now.ToString("yyyy-MM-dd");
                last.time = DateTime.Now.ToString("hh:mm tt");
                last.action = false;
                last.icon = "success";
            }

            UpdateLast(shipment.Sender_Log);
            UpdateLast(shipment.Receiver_Log);

            // -------- ADD NEW LOG ENTRY (Returned Shipment) --------
            var newSenderLog = new LogEntry
            {
                title = "Shipment Aborted",
                icon = "success",
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                time = DateTime.Now.ToString("hh:mm tt"),
                action = false
            };

            var newReceiverLog = new LogEntry
            {
                title = "Shipment Aborted",
                icon = "success",
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                time = DateTime.Now.ToString("hh:mm tt"),
                action = false
            };

            shipment.Sender_Log.Add(newSenderLog);
            shipment.Receiver_Log.Add(newReceiverLog);

            // -------- UPDATE STATUS --------
            shipment.Status = "Aborted";

            await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, shipment.Status)
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new { success = true, message = "Shipment marked as Abort." });
        }

        [HttpPut("{id}/status-destroyed")]
        public async Task<IActionResult> MarkDestroyed(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            // -------- UPDATE LAST LOG --------
            void UpdateLast(List<LogEntry> logs)
            {
                if (logs.Count == 0) return;

                var last = logs.Last();
                last.date = DateTime.Now.ToString("yyyy-MM-dd");
                last.time = DateTime.Now.ToString("hh:mm tt");
                last.action = false;
                last.icon = "success";
            }

            UpdateLast(shipment.Sender_Log);
            UpdateLast(shipment.Receiver_Log);

            // -------- ADD NEW LOG ENTRY (Destroyed Shipment) --------
            var newSenderLog = new LogEntry
            {
                title = "Destroyed Shipment",
                icon = "success",
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                time = DateTime.Now.ToString("hh:mm tt"),
                action = false
            };

            var newReceiverLog = new LogEntry
            {
                title = "Destroyed Shipment",
                icon = "success",
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                time = DateTime.Now.ToString("hh:mm tt"),
                action = false
            };

            shipment.Sender_Log.Add(newSenderLog);
            shipment.Receiver_Log.Add(newReceiverLog);

            // -------- UPDATE STATUS --------
            shipment.Status = "Destroyed";

            await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, shipment.Status)
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new { success = true, message = "Shipment marked as Destroyed." });
        }


        // ----------------------------------------------------
        // UPDATE STATUS + LOG
        // ----------------------------------------------------
        [HttpPut("{id}/status-addition-doc")]
        public async Task<IActionResult> AdditionDoc(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            var lastSender = shipment.Sender_Log.LastOrDefault();
            var lastReceiver = shipment.Receiver_Log.LastOrDefault();

            if (lastSender == null || lastReceiver == null)
                return BadRequest("No logs found");

            // ------------------ Determine Status ---------------------
            string newStatus;

            if (lastSender.title == "Customs Export")
            {
                newStatus = "Additional Document Required";
            }
            else if (lastSender.title == "Arrived at Customs")
            {
                newStatus = "Import Clearance";
            }
            else
            {
                return BadRequest("Invalid stage for Additional Doc Request");
            }

            // ------------------ Update Sender Log ---------------------
            void UpdateLastLogSender(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0) return;

                var last = logs.Last();

                last.icon = "error";
                last.date = DateTime.Now.ToString("yyyy-MM-dd");
                last.time = DateTime.Now.ToString("hh:mm tt");
                last.action = true;
                last.actionLabel = "Provide Additional Documents";
                last.action_href = "/user/resolution/sender/" + id;
            }

            // ------------------ Update Receiver Log ---------------------
            void UpdateLastLogReceiver(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0) return;

                var last = logs.Last();

                last.icon = "error";
                last.date = DateTime.Now.ToString("yyyy-MM-dd");
                last.time = DateTime.Now.ToString("hh:mm tt");
                last.action = true;
                last.actionLabel = "Provide Additional Documents";
                last.action_href = "/user/resolution/receiver/" + id;
            }

            UpdateLastLogSender(shipment.Sender_Log);
            UpdateLastLogReceiver(shipment.Receiver_Log);

            // Reset additional docs list
            shipment.Additional_docs = new List<Additional_docs>();

            // ------------------ Update DB ---------------------
            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, newStatus)
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Set(s => s.Additional_docs, shipment.Additional_docs)
                .Update();

            // ------------------ Return correct message ---------------------
            return Ok(new
            {
                success = true,
                message = newStatus
            });
        }


        [HttpPut("{id}/status-export")] // for the custom hold and approve buttons
        public async Task<IActionResult> StatusExport(int id, [FromBody] string status)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");


            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            void UpdateLastLog(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                if (last.title == "Customs Export")
                {
                    last.icon = "success";
                    last.date = DateTime.Now.ToString("yyyy-MM-dd");
                    last.time = DateTime.Now.ToString("hh:mm tt");
                    last.action = false;
                }
            }

            UpdateLastLog(shipment.Sender_Log);
            UpdateLastLog(shipment.Receiver_Log);

            // Sender: action allowed
            var senderUploadLog = new LogEntry
            {
                title = "In Transit",
                icon = "pending",
                action = false,
            };

            // Receiver: action NOT allowed
            var receiverUploadLog = new LogEntry
            {
                title = "In Transit",
                icon = "pending",
                action = false,
            };

            shipment.Sender_Log.Add(senderUploadLog);
            shipment.Receiver_Log.Add(receiverUploadLog);

            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, "In Transit")
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "Cleared Export Customs"
            });
        }

        [HttpPut("{id}/status-reached-customs")]
        public async Task<IActionResult> ReachedCustoms(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");


            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            void UpdateLastLog(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                if (last.title == "In Transit")
                {
                    last.icon = "success";
                    last.date = DateTime.Now.ToString("yyyy-MM-dd");
                    last.time = DateTime.Now.ToString("hh:mm tt");
                    last.action = false;
                }
            }

            UpdateLastLog(shipment.Sender_Log);
            UpdateLastLog(shipment.Receiver_Log);

            // Sender: action allowed
            var senderUploadLog = new LogEntry
            {
                title = "Arrived at Customs",
                icon = "pending",
                action = false,
            };

            // Receiver: action NOT allowed
            var receiverUploadLog = new LogEntry
            {
                title = "Arrived at Customs",
                icon = "pending",
                action = false,
            };

            shipment.Sender_Log.Add(senderUploadLog);
            shipment.Receiver_Log.Add(receiverUploadLog);

            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, "Arrived at Customs")
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "Arrived at Customs"
            });
        }

        [HttpPut("{id}/status-approve-import")]
        public async Task<IActionResult> ApproveImport(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");


            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            void UpdateLastLog(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                if (last.title == "Arrived at Customs")
                {
                    last.icon = "success";
                    last.date = DateTime.Now.ToString("yyyy-MM-dd");
                    last.time = DateTime.Now.ToString("hh:mm tt");
                    last.action = false;
                }
            }

            UpdateLastLog(shipment.Sender_Log);
            UpdateLastLog(shipment.Receiver_Log);

            LogEntry senderUploadLog;
            LogEntry receiverUploadLog;

            // Sender: action allowed
            if (shipment.DutyMode == "DDP")
            {
                senderUploadLog = new LogEntry
                {

                    title = "Customs Clearance Charges",
                    icon = "pending",
                    action = true,
                    actionLabel = "Pay Duty",
                    action_href = "/user/duty/sender/" + id
                };
            }
            else
            {
                senderUploadLog = new LogEntry
                {

                    title = "Customs Clearance Charges",
                    icon = "pending",
                    action = false,
                };
            }

            // Receiver: action NOT allowed
            if (shipment.DutyMode == "DAP")
            {
                receiverUploadLog = new LogEntry
                {
                    title = "Customs Clearance Charges",
                    icon = "pending",
                    action = true,
                    actionLabel = "Pay Duty",
                    action_href = "/user/duty/receiver/" + id
                };
            }
            else
            {
                receiverUploadLog = new LogEntry
                {
                    title = "Customs Clearance Charges",
                    icon = "pending",
                    action = false,
                };
            }
                shipment.Sender_Log.Add(senderUploadLog);
            shipment.Receiver_Log.Add(receiverUploadLog);

            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, "Customs Cleared")
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "Customs Cleared"
            });
        }


        // ----------------------------------------------------
        // APPROVE HS + LOG
        // ----------------------------------------------------
        [HttpPut("{id}/hs-approve")]
        public async Task<IActionResult> ApproveHs(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");


            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            void UpdateLastLog(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                if (last.title == "HS Validation")
                {
                    last.icon = "success";
                    last.date = DateTime.Now.ToString("yyyy-MM-dd");
                    last.time = DateTime.Now.ToString("hh:mm tt");
                }
            }

            UpdateLastLog(shipment.Sender_Log);
            UpdateLastLog(shipment.Receiver_Log);

            // Sender: action allowed
            var senderUploadLog = new LogEntry
            {
                title = "Document Upload",
                icon = "pending",
                action = true,
                actionLabel = "Upload Documents",
                action_href = "/user/upload/sender/" + id
            };

            // Receiver: action NOT allowed
            var receiverUploadLog = new LogEntry
            {
                title = "Document Upload",
                icon = "pending",
                action = false,
            };

            shipment.Sender_Log.Add(senderUploadLog);
            shipment.Receiver_Log.Add(receiverUploadLog);

            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, "HS Approved")
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "HS Code Approved + Logs updated"
            });
        }


        // ----------------------------------------------------
        // SET DOCUMENTS
        // ----------------------------------------------------
        [HttpPut("{id}/set-docs")]
        public async Task<IActionResult> SetDocuments(int id, [FromBody] List<Additional_docs> newDocs)
        {
            var client = await _supabase.GetClientAsync();

            // Fetch the existing shipment
            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            // Ensure list is initialized
            shipment.Additional_docs ??= new List<Additional_docs>();

            // 🔥 APPEND NEW DOCUMENTS (Do NOT replace)
            foreach (var doc in newDocs)
            {
                shipment.Additional_docs.Add(doc);
            }

            // Update database with appended list
            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Additional_docs, shipment.Additional_docs)
                .Update();

            return Ok(new { success = true, message = "Documents appended successfully" });
        }


        // ----------------------------------------------------
        // CHANGE HS CODE + LOG
        // ----------------------------------------------------
        // ----------------------------------------------------
        // CHANGE HS CODE + AUTO APPROVE + LOG (FIXED)
        // ----------------------------------------------------
        [HttpPut("{id}/change-hscode")]
        public async Task<IActionResult> ChangeHsCode(int id, [FromBody] ChangeHsCodeRequest req)
        {
            var client = await _supabase.GetClientAsync();

            // -----------------------------
            // 1️⃣ GET PRODUCT
            // -----------------------------
            var product = await client.From<Product>()
                .Where(p => p.ShipmentId == id)
                .Single();

            if (product == null)
                return NotFound("Product not found");

            // -----------------------------
            // 2️⃣ GET SHIPMENT
            // -----------------------------
            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            // -----------------------------
            // 3️⃣ UPDATE HS CODE IN PRODUCT
            // -----------------------------
            await client
                .From<Product>()
                .Where(p => p.ShipmentId == id)
                .Set(p => p.HsCode, req.hs)           // approved HS
                .Set(p => p.SenderHs, req.senderHs)  // sender HS
                .Update();

            // -----------------------------
            // 4️⃣ LOAD PREVIOUS LOGS
            // -----------------------------
            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            // -----------------------------
            // 5️⃣ UPDATE LAST LOG (HS Validation)
            // -----------------------------
            void UpdateLastLog(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                if (last.title == "HS Validation")
                {
                    last.icon = "success";
                    last.date = DateTime.Now.ToString("yyyy-MM-dd");
                    last.time = DateTime.Now.ToString("hh:mm tt");
                }
            }

            UpdateLastLog(shipment.Sender_Log);
            UpdateLastLog(shipment.Receiver_Log);

            // -----------------------------
            // 6️⃣ ADD NEW LOGS ("Document Upload")
            // -----------------------------
            var senderUploadLog = new LogEntry
            {
                title = "Document Upload",
                icon = "pending",
                action = true,
                actionLabel = "Upload Documents",
                action_href = "/user/upload/sender/" + id
            };

            var receiverUploadLog = new LogEntry
            {
                title = "Document Upload",
                icon = "pending",
                action = false
            };

            shipment.Sender_Log.Add(senderUploadLog);
            shipment.Receiver_Log.Add(receiverUploadLog);

            // -----------------------------
            // 7️⃣ UPDATE STATUS + LOGS
            // -----------------------------
            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, "HS Approved")
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "HS Code changed & approved + logs updated"
            });
        }


        [HttpPut("{id}/reason")]
        public async Task<IActionResult> UpdateReason(int id, [FromBody] string reason)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            // update reason
            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Reason, reason)
                .Update();

            return Ok(new { success = true, message = "Reason updated successfully" });
        }

        //veiw documents
        [HttpGet("{id}/docapproval")]
        public async Task<IActionResult> DocumentApproval(int id)
        {
            var client = await _supabase.GetClientAsync();
            var returnDocs = new List<DocReturn>();


            var documents = await client
                .From<Document>()
                .Where(d => d.s_id == id)
                .Get();

            var bucket = client.Storage.From("documents");

            foreach (var doc in documents.Models)
            {
                var internalPath = doc.document_url.Replace("documents/", "");
                var publicUrl = bucket.GetPublicUrl(internalPath);

                returnDocs.Add(new DocReturn
                {
                    Title = doc.document_name,
                    PublicUrl = publicUrl
                });
            }

            return Ok(returnDocs);
        }

        //delete document 
        [HttpDelete("{id}/{documentName}")]
        public async Task<IActionResult> DeleteDocument(int id, string documentName)
        {
            try
            {
                var client = await _supabase.GetClientAsync();

                // 1. GET the document record
                var documentResult = await client
                    .From<Document>()
                    .Where(d => d.s_id == id && d.document_name == documentName)
                    .Single();

                if (documentResult == null)
                    return NotFound("Document not found");

                string filePath = documentResult.document_url; // e.g. "documents/xyz/file.pdf"

                // 2. DELETE from Supabase Storage
                var bucket = client.Storage.From("documents");
                var internalPath = filePath.Replace("documents/", "");
                var storageResponse = await bucket.Remove($"{internalPath}");

                // 3. DELETE the DB row
                await client
                    .From<Document>()
                    .Where(d => d.s_id == id && d.document_name == documentName)
                    .Delete();

                return Ok(new { message = "Document deleted successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        //Document approved update log
        [HttpPut("{id}/updatedoclog")]
        public async Task<IActionResult> UpdateDocLog(int id)
        {
            //get connection
            var client = await _supabase.GetClientAsync();


            //retrive data from db
            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();


            if (shipment == null)
                return NotFound("Shipment not found");

            //update the old log
            var S_log_last = shipment.Sender_Log.Last();
            S_log_last.icon = "success";
            S_log_last.action = false;
            S_log_last.date = DateTime.Now.ToString("yyyy-MM-dd");
            S_log_last.time = DateTime.Now.ToString("hh:mm tt");

            var R_log_last = shipment.Receiver_Log.Last();
            R_log_last.icon = "success";
            R_log_last.date = DateTime.Now.ToString("yyyy-MM-dd");
            R_log_last.time = DateTime.Now.ToString("hh:mm tt");

            //create new log
            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            var Slog = CreateLog("Payment", true, $"/user/payment/sender/{id}", "Click to Pay", "pending");
            var Rlog = CreateLog("Payment", false, null, null, "pending");
            shipment.Sender_Log.Add(Slog);
            shipment.Receiver_Log.Add(Rlog);

            //update the log in db
            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, "Document Approved")
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new { success = true, message = "Document Approved" });
        }

        // ----------------------------------------------------
        // GET PAYMENT STATUS (PAID / Pending) FROM SENDER_LOG
        // ----------------------------------------------------
        [HttpGet("{id}/payment-status")]
        public async Task<IActionResult> GetPaymentStatus(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            shipment.Sender_Log ??= new List<LogEntry>();

            // ✅ If ANY log has title "Payment" and icon "success" → PAID
            bool isPaid = shipment.Sender_Log.Any(
                log => log.title == "Payment" && log.icon == "success"
            );

            var paymentStatus = isPaid ? "PAID" : "Pending";

            return Ok(new { paymentStatus });
        }

        [HttpGet("{id}/duty-payment-status")]
        public async Task<IActionResult> GetDutyPaymentStatus(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            shipment.Sender_Log ??= new List<LogEntry>();

            // ✔ Check if duty payment success exists
            bool isPaid = shipment.Sender_Log.Any(
                log => log.title == "Customs Clearance Charges" && log.icon == "success"
            );

            var paymentStatus = isPaid ? "PAID" : "Pending";

            return Ok(new { paymentStatus });
        }


        [HttpPut("{id}/status-delivered")]
        public async Task<IActionResult> MarkDelivered(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");


            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            void UpdateLastLog(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                if (last.title == "Delivered")
                {
                    last.icon = "success";
                    last.date = DateTime.Now.ToString("yyyy-MM-dd");
                    last.time = DateTime.Now.ToString("hh:mm tt");
                    last.action = false;
                }
            }

            UpdateLastLog(shipment.Sender_Log);
            UpdateLastLog(shipment.Receiver_Log);

            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, "Delivered")
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "Delivered"
            });
        }

        [HttpPut("{id}/status-return-request")]
        public async Task<IActionResult> MarkReturnRequest(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            // -----------------------------
            // 1️⃣ Update last log entry time
            // -----------------------------
            void UpdateLastLog(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();

                // Update timestamp only (logic is adjustable)
                last.date = DateTime.Now.ToString("yyyy-MM-dd");
                last.time = DateTime.Now.ToString("hh:mm tt");
                last.action = false;
                last.icon = "success";
            }

            UpdateLastLog(shipment.Sender_Log);
            UpdateLastLog(shipment.Receiver_Log);

            // ------------------------------------------
            // 2️⃣ Add NEW LOG ENTRY (STATIC for now)
            // ------------------------------------------
            var senderLog = new LogEntry
            {
                title = "Requested Return",
                icon = "pending",
                action = false,
            };

            var receiverLog = new LogEntry
            {
                title = "Requested Return",
                icon = "pending",
                action = false,
            };

            shipment.Sender_Log.Add(senderLog);
            shipment.Receiver_Log.Add(receiverLog);

            // -----------------------------
            // 3️⃣ Update status + logs
            // -----------------------------
            shipment.Status = "Return Request";

            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, shipment.Status)
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "Return request applied successfully"
            });
        }


        [HttpPut("{id}/status-destruction-request")]
        public async Task<IActionResult> MarkDestructionRequest(int id)
        {
            var client = await _supabase.GetClientAsync();

            var shipment = await client.From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            shipment.Sender_Log ??= new List<LogEntry>();
            shipment.Receiver_Log ??= new List<LogEntry>();

            // --------------------------------------
            // 1️⃣ Update last log timestamps
            // --------------------------------------
            void UpdateLastLog(List<LogEntry> logs)
            {
                if (logs == null || logs.Count == 0)
                    return;

                var last = logs.Last();
                last.date = DateTime.Now.ToString("yyyy-MM-dd");
                last.time = DateTime.Now.ToString("hh:mm tt");
                last.icon = "success";
                last.action = false;
            }

            UpdateLastLog(shipment.Sender_Log);
            UpdateLastLog(shipment.Receiver_Log);

            // --------------------------------------
            // 2️⃣ Add NEW static log entry
            // --------------------------------------
            var senderLog = new LogEntry
            {
                title = "Requested Destruction",
                icon = "pending",
                action = false,
            };

            var receiverLog = new LogEntry
            {
                title = "Requested Destruction",
                icon = "pending",
                action = false,
            };

            shipment.Sender_Log.Add(senderLog);
            shipment.Receiver_Log.Add(receiverLog);

            // --------------------------------------
            // 3️⃣ Update status + logs in DB
            // --------------------------------------
            shipment.Status = "Destruction Request";

            await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Set(s => s.Status, shipment.Status)
                .Set(s => s.Sender_Log, shipment.Sender_Log)
                .Set(s => s.Receiver_Log, shipment.Receiver_Log)
                .Update();

            return Ok(new
            {
                success = true,
                message = "Destruction request applied successfully"
            });
        }

        [HttpGet("{id}/shipping-cost-preview")]
        public async Task<IActionResult> GetShippingCostPreview(int id)
        {
            var client = await _supabase.GetClientAsync();

            // Fetch shipment
            var shipment = await client
                .From<Shipment>()
                .Where(s => s.SId == id)
                .Single();

            if (shipment == null)
                return NotFound("Shipment not found");

            // 👉 CALL EXISTING SHIPPING SERVICE LOGIC
            // We reuse PaymentController-style calculation
            // but WITHOUT payment / razorpay

            // Fetch product + receiver
            var productDb = await client.From<Product>()
                .Where(p => p.ShipmentId == id)
                .Single();

            var receiverDb = await client.From<Receiver>()
                .Where(r => r.ShipmentId == id)
                .Single();

            if (productDb == null || receiverDb == null)
                return BadRequest("Missing product or receiver data");

            // -------- SHIPPING BASE --------
            decimal shipping = new ShippingCostService().Calculate(new ShippingInput
            {
                Country = receiverDb.Country ?? "",
                Weight = productDb.Weight ?? 0,
                Qty = productDb.NoOfPackages ?? 1,
                L = productDb.Length ?? 10,
                W = productDb.Width ?? 10,
                H = productDb.Height ?? 10,
                Unit = productDb.Unit ?? "cm"
            });

            double baseShipping = Math.Round((double)shipping);

            // -------- FINE LOGIC (reuse your rules) --------
            double fine = 0;
            int daysPassed = 0;

            if (shipment.Sender_Log != null)
            {
                var docUpload = shipment.Sender_Log
                    .FirstOrDefault(l => l.title == "Document Upload");

                if (docUpload?.date != null)
                {
                    if (DateTime.TryParse(docUpload.date, out DateTime uploadDate))
                    {
                        daysPassed = (DateTime.Now - uploadDate).Days;

                        if (daysPassed > 90)
                        {
                            return Ok(new
                            {
                                cancelled = true,
                                message = "Shipment cancelled due to delay > 90 days"
                            });
                        }

                        if (daysPassed > 5)
                        {
                            fine = 75 + ((daysPassed - 5) * 0.005 * baseShipping);
                        }
                    }
                }
            }

            long totalLong = (long)Math.Round(baseShipping + fine);



            return Ok(new
            {
                baseShipping,
                fine,
                totalLong,
                daysPassed
            });

        }

    }
}
