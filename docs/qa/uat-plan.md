# แผนการทดสอบ UAT — Sprint 16A (S16-QA-01)

แผนการทดสอบเพื่อรับมอบระบบ (User Acceptance Test) สำหรับ **CM+ Project Control** ก่อนขอ human
sign-off ตาม **US-16.1**. รันบน staging ที่ตั้งขึ้นตาม S16-DO-01 (topology เท่าที่ production จะเป็น)
โดยผู้ใช้หน้างานจริง

> **หลักการ:** ทุก scenario ผูกกับ user story (US) และ acceptance criteria จริงใน
> `docs/specs/master-plan/backlog-detailed.md` — คอลัมน์ "ผลที่คาดหวัง" คือ AC ของ story นั้น ไม่ใช่
> ข้อความทั่วไป ผู้ทดสอบทำตามขั้นตอนแล้วบันทึกผลว่า **ผ่าน / ไม่ผ่าน** พร้อมหลักฐาน

---

## 1. ก่อนเริ่ม (Preconditions)

- **URL:** staging endpoint (จาก S16-DO-01) ผ่าน HTTPS เท่านั้น
- **บัญชีทดสอบ:** อย่างน้อย 1 บัญชีต่อ role ที่เกี่ยวข้องกับ approval matrix — Site Engineer / PM /
  Project Director (และ Tenant Admin สำหรับหน้า admin) จาก tenant ทดสอบ **สองราย** (เพื่อพิสูจน์
  tenant isolation ในข้อ 6.3)
- **ข้อมูลตั้งต้น:** โครงการตัวอย่างหนึ่งโครงการที่ import ตาราง P6/MSPDI แล้ว มี WBS/Activity, baseline,
  และ progress อย่างน้อยหนึ่งงวด
- **อุปกรณ์:** เครื่อง desktop (จอกว้างสำหรับ Gantt) และมือถือ/แท็บเล็ตหนึ่งเครื่อง (สำหรับ Photo offline
  ข้อ 5.11 และ PWA ข้อ 6.1)

## 2. วิธีบันทึกผลและ defect (dogfood)

- แต่ละ scenario บันทึก: **ผ่าน / ไม่ผ่าน / ติดขัด**, ผู้ทดสอบ, วันที่-เวลา, และ screenshot/หมายเหตุ
- **ข้อบกพร่องที่พบให้บันทึกลง Issue/Action Log ของระบบเอง** (โมดูล Issue — ทดสอบระบบด้วยการใช้ระบบ,
  ตาม DoD ของ S16-QA-01) พร้อม severity; blocker ต้องปิด 100% ก่อนขอ sign-off (ตาม S16-BE/FE-01)
- สรุปผลรวมไปที่ `docs/qa/uat-results.md`

## 3. เกณฑ์ระดับความรุนแรง (Severity)

| ระดับ | นิยาม | เงื่อนไข sign-off |
| :-- | :-- | :-- |
| **Blocker** | ทำงานหลักไม่ได้ / ข้อมูลผิด / ตัวเลขเงิน-เวลา-สิทธิ์ผิด | ปิดครบ 100% |
| **Major** | ทำงานได้แต่มีทางเลี่ยง / UX เสียหายชัด | มีเจ้าของ + กำหนดเวลา |
| **Minor** | cosmetic / ข้อความ | บันทึกไว้ |

---

## 4. Test accounts & roles (ตรวจสิทธิ์)

| Scenario | US | ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- | :-- | :-- |
| 4.1 Login | US-2.1 | เข้าสู่ระบบด้วยบัญชีที่ถูกต้อง | ได้ token; เห็นเมนูตามสิทธิ์ของ role นั้น | |
| 4.2 Login ผิด | US-2.1 | ใส่รหัสผิด และใส่อีเมลที่ไม่มี | ได้ข้อความ error เดียวกันทั้งสองกรณี (ไม่บอกว่าอีเมลมี/ไม่มีในระบบ) | |
| 4.3 สิทธิ์ตาม role | US-2.1 | ล็อกอินเป็น Site Engineer แล้วลองเข้าหน้า Tenant Admin | ถูกปฏิเสธ (ไม่เห็น/เข้าไม่ได้) | |

---

## 5. Scenario ต่อหน้าจอ (15 โมดูล / 13 screens)

### 5.1 Project Info (`info`) — US-4.3, US-4.4
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| เปิดหน้า Project Info แก้ไขข้อมูลโครงการแล้วบันทึก | บันทึกสำเร็จ และมี **AuditLog** ถูกเขียน (US-2.2) | |
| ตั้งค่า **Retention rate** และ **Advance rate** ของโครงการ | ค่าถูกเก็บเป็น `decimal(5,2)` และถูกใช้จริงในการคิด Payment (ดู 5.9) | |

### 5.2 WBS & Activity (`wbs`) — US-4.1, US-4.5
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| เปิด WBS tree ของโครงการใหญ่ | โหลดเร็ว (เป้าหมาย < 100 ms ฝั่ง API), ต้นไม้ครบ | |
| เข้าโหมด **"Update Progress"** แบบ batch อัปเดตหลาย activity แล้วบันทึก | สร้าง `ActivityProgressLog` หลายแถว (append) แต่ละแถว stamp เวลาเดียวกัน; ไม่ใช่แค่แก้ค่าล่าสุด (US-1.2) | |

### 5.3 Gantt / CPM (`gantt`) — US-5.1, US-6.1, US-6.2
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| เปิด Gantt | แสดงเส้นทาง **critical / non-critical / baseline / data-date** แยกสีชัด | |
| เลื่อน/ซูมบนโครงการ 10,000+ activity | ลื่นไหล ไม่ค้าง (virtualized) | |
| ตรวจ float/total float ของ activity ที่รู้ค่า | ตรงกับผล CPM (forward/backward pass, US-5.1; reconcile P6 US-5.2) | |

### 5.4 S-Curve (`evm` chart) — US-7.3
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| เปิด S-Curve | เห็นเส้น **PV / EV / AC** และเส้น **EAC forecast** ตาม variant ที่เลือก | |

### 5.5 EVM (`evm`) — US-7.1, US-7.2
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| เปิดหน้า EVM ของงวดที่มีข้อมูล | แสดง PV/EV/AC/SV/CV/SPI/CPI ถูกต้องตามสูตร (`evm-formulas.md`) | |
| เปลี่ยน **EAC variant** และตั้ง project default | EAC เปลี่ยนตามสูตรของ variant นั้น (5 ตัวเลือก, ADR-0007); ค่า default ถูกจำ | |

### 5.6 Dashboard (`dashboard`) — US-8.1, US-8.3
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| เปิด Executive Dashboard | KPI tiles ใช้ **DefaultEacVariant** ของโครงการ พร้อม subtitle สูตร | |
| ตรวจ WBS progress rollup | ยอดรวมความก้าวหน้าตรงกับผลรวมของ activity (US-8.3) | |

### 5.7 Cash Flow (`cash`) — US-8.2
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| เปิด Cash Flow | กระแสเงินสดสอดคล้องกับ PV/แผนงาน (US-8.2) | |

### 5.8 Executive Summary PDF (`dashboard`/export) — US-8.4
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| กด export Executive Summary เป็น PDF | ได้ไฟล์ PDF ที่มีเนื้อหาสรุปครบ เปิดอ่านได้ | |

### 5.9 Payment (`payment`) — US-9.1, US-9.2, US-9.3
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| สร้าง Payment Certificate | ใช้ **Retention/Advance ที่ตั้งไว้ในโครงการ** (US-9.1) คิดเงินถูกต้อง `decimal(18,2)` | |
| ส่งอนุมัติ Payment | เดินตาม **approval matrix** ตามยอดเงิน ไม่ใช่ role ตายตัว (US-9.3); chain ถูก snapshot ตอน submit | |

### 5.10 Variation Order (`vo`) — US-10.1, US-10.2, US-10.3
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| สร้าง VO (เพิ่ม/ลดมูลค่า) | คำนวณผลกระทบต่อ **BAC** ถูกต้อง (บวก/ลบตามเครื่องหมาย) | |
| ส่ง VO อนุมัติผ่าน matrix แล้วอนุมัติ | ใช้ matrix เดียวกับ Payment (US-10.2); เมื่ออนุมัติแล้ว **S-Curve/EVM rebaseline** (US-10.3) | |
| ลองแก้ BAC โดยตรงหลังมี VO อนุมัติแล้ว | ถูกบล็อก (ADR-0017) | |

### 5.11 Weather Log / EOT (`weather`) — US-11.1
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| บันทึก Weather Log แล้วลองแก้/ลบรายการเดิม | รายการเดิม **แก้ไม่ได้** (immutable legal evidence, US-11.1); การแก้ทำได้เฉพาะออกรายการ correction ใหม่ | |
| รันประเมิน EOT | นับวันหยุดจริงแบบ absolute (ADR-0020) ได้จำนวนวันตามข้อมูล | |

### 5.12 Issue / Action Log (`issue`) — US-11.2
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| สร้าง Issue แล้วเลื่อนสถานะตาม flow | สถานะเดินตามลำดับที่กำหนด (US-11.2); (ใช้จริงในการ log defect ของ UAT นี้) | |

### 5.13 Photo Progress (`photo`) — US-12.1
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| บนมือถือ **ปิดเน็ต** แล้วถ่าย/แนบรูป progress | บันทึกได้ขณะ offline (เข้า outbox) | |
| เปิดเน็ตอีกครั้ง | รูป sync ขึ้น server อัตโนมัติ ไม่สูญหาย ไม่ซ้ำ (US-12.1) | |

### 5.14 Man / Equipment (`maneq`) — US-12.2
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| บันทึก daily log กำลังคน/เครื่องจักร | เก็บค่าถูกต้อง | |
| ดู **Productivity Index** | เป็นค่า earned/actual man-hours (EV/AC analogue) ไม่ใช่ manning ratio — วันที่กำลังคนเกินแผนแต่ผลงานช้าต้องอ่าน "แย่กว่าแผน" (ดู lessons 2026-08-10) | |

### 5.15 Baseline (`baseline`) — US-14.1, US-14.2
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| บันทึกและ **activate** baseline | มี baseline ที่ active เพียงหนึ่งเดียวต่อโครงการ (filtered-unique index) | |
| เปิด **delta comparison** เทียบ current กับ baseline | แสดงส่วนต่าง (วันที่/ความก้าวหน้า) ถูกต้อง (US-14.2) | |

### 5.16 Tenant Admin: Approval Matrix (`tenant-admin`) — US-9.4, US-9.5
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| เข้าหน้า Tenant Admin แก้ approval matrix | ดู/แก้ได้ (เฉพาะ Tenant Admin, US-9.4); เป็น tenant-wide (US-9.5) | |
| แก้ threshold แล้วสร้างเอกสารทดสอบ | เอกสารใหม่ route ตาม matrix เวอร์ชันที่ pin ตอน submit | |

---

## 6. Cross-cutting (ต้องทดสอบเสมอ)

### 6.1 PWA / Offline (US-13.1)
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| ปิดเน็ตแล้วทำงานเขียนข้อมูล (Photo, Weather, Progress) | เข้า outbox ได้ทั้งหมด (US-13.1) | |
| เปิดเน็ต | sync ครบ ไม่ซ้ำ ไม่ข้าม | |
| ออกจากระบบขณะยังมีรายการค้าง sync | มีคำเตือนก่อน sign-out (S12 N-03) | |
| deploy build ใหม่ | ผู้ใช้เดิมได้เวอร์ชันใหม่ (Service Worker cache-bust ด้วย VITE_BUILD_ID) ไม่ค้างเวอร์ชันเก่า | |

### 6.2 Approval workflow (ข้ามโมดูล)
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| ส่งเอกสารเมื่อ tenant ไม่มี policy สำหรับประเภทนั้นเลย | ถูกบล็อก/ใช้ fallback ที่เข้มงวด — **ไม่ auto-approve** (§5.3 step 6) | |
| ส่งเอกสารเมื่อมี policy แต่ไม่ตรง scope โครงการ | ถูกบล็อกด้วย PolicyGap (fail closed, N-06) ไม่หลุดไปใช้ fallback | |
| ผู้ยื่นลองอนุมัติเอกสารตนเอง | ถูกปฏิเสธถ้า policy ไม่อนุญาต self-approval | |

### 6.3 Multi-tenant isolation (US-15.1) — **Blocker หากล้มเหลว**
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| ล็อกอิน tenant A จด id ทรัพยากรของ tenant B แล้วเรียกตรง ๆ | ได้ 404/403 ไม่เคยเห็นข้อมูลของ B (US-15.1) | |
| ทุกหน้าจอรายการ | เห็นเฉพาะข้อมูลของ tenant ตนเอง | |

### 6.4 ความปลอดภัยระดับ HTTP (อ้างอิง S16-SEC-01)
| ขั้นตอน | ผลที่คาดหวัง | ผล |
| :-- | :-- | :-- |
| ยิงล็อกอินผิดซ้ำ ๆ จากเครื่องเดียว | ถูก 429 + Retry-After (rate limit); **และต้องไม่กระทบผู้ใช้เครื่องอื่น** (ตรวจ F-1 ForwardedHeaders บน staging) | |
| ดู response header ของ API | มี `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy` (L-06); ตอบผ่าน HTTPS มี HSTS | |
| ทำให้เกิด error | ไม่มี stack trace/รายละเอียดภายในระบบรั่วออกมา (เห็นเฉพาะ ProblemDetails + error code) | |

---

## 7. เกณฑ์ผ่าน (Exit criteria สำหรับ US-16.1 sign-off)

- [ ] ทุก scenario ข้อ 4–6 มีผล **ผ่าน** หรือมี defect ที่บันทึกและ triage แล้ว
- [ ] **Blocker = 0** ก่อนขอ sign-off (S16-BE/FE-01)
- [ ] ข้อ 6.3 (tenant isolation) ผ่านทั้งหมด — ถ้าไม่ผ่าน **หยุด**, ถือเป็น release blocker
- [ ] ข้อ 6.4 F-1 (rate limit หลัง proxy) ได้รับการยืนยันบน staging (prod blocker จาก S16-SEC-01)
- [ ] ผลทั้งหมดสรุปใน `docs/qa/uat-results.md` พร้อมผู้อนุมัติ + วันที่
- [ ] Smoke test อัตโนมัติ (S16-QA-02) เขียว หลัง deploy

*จัดทำตาม S16-QA-01 · ทุก scenario ผูกกับ US จริงใน `docs/specs/master-plan/backlog-detailed.md`*
