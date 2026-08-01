# Excel AI Kategorizator

Excel (`.xlsx`) fayllardagi matnli ma'lumotlarni sun'iy intellekt yordamida
avtomatik kategoriyalarga ajratuvchi ASP.NET Core MVC veb-ilovasi.

> 💡 **Bepul ishlaydi.** Standart sozlamada Google Gemini'ning bepul tarifi
> ishlatiladi — bank kartasi talab qilinmaydi. Groq, OpenRouter, lokal Ollama
> yoki pullik Anthropic Claude ham qo'llab-quvvatlanadi (6.2-bo'limga qarang).

Foydalanuvchi fayl yuklaydi → har bir qator AI tomonidan tahlil qilinadi →
natija **kategoriya**, **ishonch darajasi** va **izoh** ustunlari qo'shilgan yangi
Excel fayl sifatida qaytariladi.

---

## Mundarija

1. [Loyihaning vazifasi](#1-loyihaning-vazifasi)
2. [Arxitektura](#2-arxitektura)
3. [Fayl tuzilmasi](#3-fayl-tuzilmasi)
4. [Komponentlar batafsil](#4-komponentlar-batafsil)
5. [AI integratsiyasi qanday ishlaydi](#5-ai-integratsiyasi-qanday-ishlaydi)
6. [Ishga tushirish](#6-ishga-tushirish)
7. [Sozlamalar (`appsettings.json`)](#7-sozlamalar-appsettingsjson)
8. [Chiqish fayl formati](#8-chiqish-fayl-formati)
9. [Xatoliklar va yechimlar](#9-xatoliklar-va-yechimlar)
10. [Xarajat va unumdorlik](#10-xarajat-va-unumdorlik)
11. [Kengaytirish yo'nalishlari](#11-kengaytirish-yonalishlari)

---

## 1. Loyihaning vazifasi

Qo'lda minglab qatorni o'qib chiqib "bu shikoyat, bu taklif, bu texnik muammo" deb
belgilash — soatlab vaqt oladigan ish. Bu ilova shu jarayonni avtomatlashtiradi.

**Asosiy imkoniyatlar:**

| Imkoniyat | Tavsif |
|---|---|
| Qat'iy kategoriyalar | Siz bergan ro'yxatdan tashqariga chiqish **texnik jihatdan imkonsiz** (JSON Schema `enum`) |
| Erkin kategoriyalar | Ro'yxat bermasangiz, AI kategoriyalarni ma'lumotning o'zidan aniqlaydi |
| Avtomatik ustun aniqlash | Ustun nomi berilmasa, matnga eng boy ustun o'zi tanlanadi |
| To'da (batch) ishlov | 25 qator bitta so'rovda → ~25× arzon va tez |
| Parallel ishlov | Bir vaqtda 3 ta to'da (sozlanadi) |
| Jonli progress | Fon ishchisi + har 2 soniyada holat so'rovi |
| Xatoga chidamlilik | Bitta to'da qulasa, qolgani davom etadi; 3 marta qayta urinish |
| Ishonch darajasi | Past ishonchli qatorlar Excel'da sariq bilan belgilanadi |
| Xulosa varag'i | Kategoriyalar bo'yicha soni va foizi |

---

## 2. Arxitektura

### Ma'lumot oqimi

```
   Brauzer
      │  POST /Home/Start  (fayl + kategoriyalar)
      ▼
┌─────────────────┐   1. faylni diskka saqlash
│ HomeController  │   2. Job yaratish
└────────┬────────┘   3. navbatga qo'yish → darhol javob qaytadi
         │
         ▼
   ┌───────────┐        ┌──────────────────────┐
   │ IJobQueue │───────▶│ CategorizationWorker │  (BackgroundService)
   └───────────┘        └──────────┬───────────┘
                                   │
        ┌──────────────────────────┼───────────────────────────┐
        ▼                          ▼                           ▼
  ┌───────────┐          ┌──────────────────┐         ┌──────────────┐
  │ExcelService│  o'qish │ Claude           │  AI     │ ExcelService │  yozish
  │  .Read()   │────────▶│ CategorizationSvc│────────▶│  .Write()    │
  └───────────┘          └──────────────────┘         └──────────────┘
                                   │
                                   ▼
                            ┌─────────────┐
                            │  IJobStore  │◀── GET /Home/Status/{id}  (polling)
                            └─────────────┘
                                   │
                                   ▼
                          GET /Home/Download/{id}
```

### Nima uchun fon ishchisi?

10 000 qatorli fayl ≈ 400 ta AI so'rovi ≈ bir necha daqiqa. HTTP so'rovi ichida
buni bajarish timeout bilan tugaydi. Shuning uchun:

- Controller faqat **navbatga qo'yadi** va darhol progress sahifasiga yo'naltiradi.
- `CategorizationWorker` (`BackgroundService`) vazifani mustaqil bajaradi.
- Brauzer `/Home/Status/{id}` orqali holatni kuzatib turadi.

---

## 3. Fayl tuzilmasi

```
ExcelAiCategorizer/
├── Program.cs                          # DI ro'yxati va HTTP pipeline
├── appsettings.json                    # Barcha sozlamalar
│
├── Models/
│   ├── AppSettings.cs                  # AiSettings, UploadSettings
│   ├── ExcelModels.cs                  # ExcelTable, ExcelRowItem
│   ├── CategorizationModels.cs         # CategorizationOptions, CategoryAssignment
│   ├── JobModels.cs                    # CategorizationJob, JobStatus, JobStatusDto
│   └── UploadViewModel.cs              # Forma modeli + validatsiya
│
├── Services/
│   ├── IExcelService.cs / ExcelService.cs
│   ├── IAiCategorizationService.cs     # Umumiy interfeys
│   ├── CategorizationPrompt.cs         # Prompt + JSON sxema + javob tozalash
│   ├── OpenAiCompatibleCategorizationService.cs   # Gemini/Groq/Ollama (bepul)
│   ├── ClaudeCategorizationService.cs  # Anthropic SDK (pullik)
│   ├── RequestRateLimiter.cs           # Bepul limitlar uchun pauza
│   ├── JobStore.cs                     # IJobStore + InMemoryJobStore
│   ├── JobQueue.cs                     # IJobQueue + ChannelJobQueue
│   ├── FileStorage.cs                  # Diskda saqlash
│   ├── CategorizationWorker.cs         # Asosiy fon ishchisi
│   └── CleanupService.cs               # Eski fayllarni tozalash
│
├── Controllers/
│   └── HomeController.cs               # 5 ta endpoint
│
├── Views/Home/
│   ├── Index.cshtml                    # Yuklash formasi
│   └── Progress.cshtml                 # Jonli progress + xulosa
│
└── App_Data/                           # Avtomatik yaratiladi (git'ga qo'shmang!)
    ├── uploads/                        # Vaqtinchalik manba fayllar
    └── results/                        # Tayyor natijalar
```

---

## 4. Komponentlar batafsil

### 4.1 `ExcelService` — Excel o'qish/yozish

**`Read(Stream, string? columnSpec)`**

1. Birinchi varaqni ochadi, `RangeUsed()` orqali to'ldirilgan diapazonni oladi.
2. **Sarlavha qatorini avtomatik topadi** (quyida batafsil).
3. Butunlay bo'sh qatorlarni tashlab ketadi.
4. **Ustun(lar)ni aniqlaydi** (quyida batafsil).
5. Tanlangan ustunlarda matni bo'lmagan qatorlarni chiqarib tashlaydi.

#### Sarlavha qatorini topish

1-qator har doim ham sarlavha bo'lavermaydi. Telegram/funstat, 1C va boshqa
tizimlarning eksportlarida yuqorida banner, eksport sanasi va parametrlar
bloki bo'ladi:

```
Qator 1 : User groups export by funstat        ← banner
Qator 2 : Export date            6/28/2026     ← metama'lumot
Qator 4 : Parameters                           ← blok sarlavhasi
Qator 5 : Name        .                        ← parametr
Qator 9 : ID | group title | username | link   ← HAQIQIY SARLAVHA
Qator 10: 1808785023 | IMPERIA ... | @Imper... ← ma'lumot
```

Algoritm: birinchi 25 qatorni tekshiradi va **o'zi ham, keyingi qator ham eng ko'p
to'ldirilgan** qatorni tanlaydi (`score = min(joriy, keyingi)`). Banner qatorlarida
1-2 ta katak to'ldirilgan bo'lgani uchun ular avtomatik chetlab o'tiladi.

#### Ustun(lar)ni tanlash

| Kiritilgan qiymat | Natija |
|---|---|
| *(bo'sh)* | Matnga eng boy bitta ustun avtomatik tanlanadi |
| `Izoh` | Faqat shu ustun |
| `Qiziqish, Yo'nalish, Motivatsiya` | Uchalasi birlashtirilib tahlil qilinadi |
| `*` yoki `barcha` / `hammasi` / `all` | To'ldirilgan barcha ustunlar |

Avtomatik tanlash: 200 ta namunaviy qatorni tekshiradi, 50%+ raqamli ustunlarni
chetlab o'tadi, qolganidan **o'rtacha matn uzunligi × to'ldirilganlik** ko'rsatkichi
eng yuqorisini oladi.

Bir nechta ustun tanlanganda AI ga `Ustun: qiymat` satrlari yuboriladi:

```
Qiziqish: Backend C#
Yo'nalish: IT dasturlash bo'yicha valontyor bo'lmoqchiman
Motivatsiya: tajribamni oshirish uchun
```

> ⚠️ **Avtomatik tanlash barqaror emas.** U eng uzun matnli ustunni oladi, va bu
> fayldan faylga o'zgarishi mumkin. Bir nechta faylni bir xil mezon bo'yicha
> tahlil qilmoqchi bo'lsangiz, ustun nomini aniq yozing.

Cheklovlar: maksimal **20 000 qator**, har bir matn **2 000 belgi** (kesiladi).

**`Write(table, results, out summary)`**

Ikkita varaqli yangi kitob yaratadi:

- **`Natija`** — asl ustunlar + `AI kategoriya`, `Ishonch`, `Izoh`.
  Sarlavha muzlatiladi (`FreezeRows(1)`), avtofiltr qo'yiladi,
  ishonch `0.6` dan past bo'lsa katak sariq (`#FDE68A`),
  tahlil qilinmagan qator qizil (`#FECACA`) bilan belgilanadi.
- **`Xulosa`** — manba varaq, tahlil qilingan ustun, sana va
  kategoriyalar bo'yicha soni/foizi jadvali.

### 4.2 `ClaudeCategorizationService` — AI qatlami

Bitta to'dani AI ga yuboradi va tozalangan natija qaytaradi. Batafsil 5-bo'limda.

### 4.3 `CategorizationWorker` — orkestrator

```csharp
1. Excel o'qish                      → ExcelTable
2. Chunk(BatchSize)                  → to'dalar ro'yxati
3. Parallel.ForEachAsync(            → cheklangan parallellik
       MaxDegreeOfParallelism = MaxParallelBatches)
4. ConcurrentDictionary'ga yig'ish   → natijalar
5. ExcelService.Write()              → bayt massivi
6. Diskka saqlash + Status = Completed
```

**Xatoga chidamlilik:** bitta to'da butunlay muvaffaqiyatsiz bo'lsa
(`MaxRetries` dan keyin ham), u `FailedRows` ga qo'shiladi va **jarayon davom etadi**.
Shu qatorlar natija faylida "Tahlil qilinmadi" deb belgilanadi.

### 4.4 `IJobQueue` / `IJobStore`

- `ChannelJobQueue` — `System.Threading.Channels` asosidagi thread-safe navbat.
  Controller yozadi, worker o'qiydi.
- `InMemoryJobStore` — `ConcurrentDictionary`. Bitta server nusxasi uchun yetarli.
  Bir nechta serverga kengaytirilganda Redis/SQL bilan almashtiriladi.

> ⚠️ Ilova qayta ishga tushsa, tugallanmagan vazifalar yo'qoladi. Bu ataylab
> qilingan soddalashtirish — davomiylik kerak bo'lsa 11-bo'limga qarang.

### 4.5 `FileStorage`

Fayl nomi sifatida **faqat GUID** ishlatiladi — foydalanuvchi bergan fayl nomi
hech qachon disk yo'liga qo'shilmaydi (*path traversal* himoyasi).
Manba fayl tahlil tugagach darhol o'chiriladi.

### 4.6 `CleanupService`

Har 30 daqiqada `ResultRetentionHours` dan eski vazifalarni va ularning
fayllarini o'chiradi.

### 4.7 `HomeController`

| Endpoint | Metod | Vazifa |
|---|---|---|
| `/` | GET | Yuklash formasi |
| `/Home/Start` | POST | Validatsiya → saqlash → navbat → redirect |
| `/Home/Progress/{id}` | GET | Progress sahifasi |
| `/Home/Status/{id}` | GET | JSON holat (polling uchun) |
| `/Home/Download/{id}` | GET | Natija `.xlsx` |

---

## 5. AI integratsiyasi qanday ishlaydi

### 5.1 Ikkita implementatsiya, bitta interfeys

`IAiCategorizationService` interfeysining ikkita amalga oshirilishi bor.
Qaysi biri ishlashini `Ai:Provider` sozlamasi hal qiladi:

| `Provider` | Klass | Kimlar uchun |
|---|---|---|
| `OpenAiCompatible` *(standart)* | `OpenAiCompatibleCategorizationService` | Gemini, Groq, OpenRouter, Ollama, Mistral — **bepul variantlar** |
| `Anthropic` | `ClaudeCategorizationService` | Rasmiy Claude SDK (pullik, eng yuqori sifat) |

Ikkalasi ham `CategorizationPrompt` klassidan bir xil prompt va JSON sxemani oladi —
shuning uchun provayderni almashtirganda natija taqqoslanadigan bo'ladi.

**OpenAI-mos so'rov** (`POST {BaseUrl}/chat/completions`):

```jsonc
{
  "model": "gemini-2.5-flash",
  "messages": [
    { "role": "system", "content": "<ko'rsatmalar + kategoriyalar>" },
    { "role": "user",   "content": "[{\"row\":1,\"text\":\"...\"}]" }
  ],
  "temperature": 0,
  "max_tokens": 8000,
  "response_format": { "type": "json_object" }
}
```

**Anthropic so'rovi** (rasmiy SDK):

```csharp
new MessageCreateParams
{
    Model      = "claude-opus-5",
    MaxTokens  = 8000,
    System     = [ new() { Text = systemPrompt,
                           CacheControl = new CacheControlEphemeral() } ],
    OutputConfig = new OutputConfig
    {
        Effort = Effort.Medium,
        Format = new JsonOutputFormat { Schema = schema }
    },
    Messages = [ new() { Role = Role.User, Content = qatorlarJson } ]
}
```

### 5.1a Bepul modellar bilan ishlashning o'ziga xosligi

Bepul/kichik modellar Claude kabi intizomli emas. Kod shuni hisobga oladi:

| Muammo | Yechim (kodda) |
|---|---|
| Javobni <code>```json ... ```</code> ichiga o'raydi | `ExtractJson()` — kod bloklarini tozalaydi, birinchi `{` dan oxirgi `}` gacha oladi |
| JSON oldidan "Mana natija:" deb yozadi | Yuqoridagi bilan bir xil |
| `enum` ni e'tiborsiz qoldirib, ro'yxatdan tashqari kategoriya beradi | `Normalize()` — qat'iy rejimda "Aniqlanmadi" ga o'tkazadi |
| Kategoriya nomini kichik harf bilan yozadi | `Normalize()` — ro'yxatdagi yozilishga moslaydi |
| `confidence` ni 1.5 deb qaytaradi | `Math.Clamp(0, 1)` |
| Ba'zi qatorlarni tashlab ketadi | Yetishmagan qatorlar `FailedRows` ga qo'shiladi, faylda belgilanadi |
| Daqiqadagi so'rov limiti (429) | `RequestRateLimiter` — so'rovlar orasiga avtomatik pauza |

Shuning uchun bepul tarifda `BatchSize` **20** (Claude'dagi 25 emas) va
`MaxParallelBatches` **1** qilib qo'yilgan.

### 5.2 Structured output — eng muhim qism

Model javobi **JSON Schema** bilan majburlanadi. Bu "modeldan JSON so'rash" emas —
API darajasidagi kafolat: model sxemaga mos kelmaydigan javob **qaytara olmaydi**.

```jsonc
{
  "type": "object",
  "properties": {
    "items": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "row":        { "type": "integer" },
          "category":   { "type": "string", "enum": ["Shikoyat", "Taklif", ...] },
          "confidence": { "type": "number" },
          "reason":     { "type": "string" }
        },
        "required": ["row", "category", "confidence", "reason"],
        "additionalProperties": false
      }
    }
  },
  "required": ["items"],
  "additionalProperties": false
}
```

`enum` faqat **"Yangi kategoriya yaratishga ruxsat"** belgilanmagan holatda
qo'shiladi — shunda model ro'yxatdan chiqishi mumkin emas.

### 5.3 Prompt tuzilishi

| Qism | Mazmuni | Nima uchun shu yerda |
|---|---|---|
| **System** | Rol, kontekst, kategoriyalar ro'yxati, 5 ta qat'iy qoida | Bitta fayl davomida **o'zgarmaydi** → `cache_control` bilan barcha to'dalarda qayta ishlatiladi |
| **User** | `[{"row":1,"text":"..."}, ...]` | Har to'dada o'zgaradi |

System promptdagi qoidalar:

1. Har bir qator uchun aynan bitta natija — birortasini ham tashlab ketma.
2. `row` kirishdagi raqam bilan bir xil bo'lishi shart.
3. `confidence` — 0.0–1.0; ikkilanayotgan bo'lsa pastroq baho.
4. `reason` — o'zbekcha, 20 so'zdan oshmasin.
5. Faqat matndagi ma'lumotga tayan, taxmin qilma.

### 5.4 Javobni tozalash (`Normalize`)

Model ishonchli bo'lsa ham, javob tekshiriladi:

- To'dada bo'lmagan `row` raqamlari tashlab yuboriladi
- Takroriy `row` lar olib tashlanadi
- `confidence` majburan `0.0..1.0` oralig'iga siqiladi
- Bo'sh kategoriya → `"Aniqlanmadi"`

### 5.5 Qayta urinish (retry)

Quyidagi xatolarda **eksponensial kutish** (2s → 4s → 8s) bilan qayta urinadi:

| Xato | Sabab |
|---|---|
| `AnthropicRateLimitException` | 429 — limitga yetildi |
| `Anthropic5xxException` | 500 / 529 — server bandligi |
| `HttpRequestException` | Tarmoq uzilishi |
| `TaskCanceledException` | Timeout |
| `JsonException` | Buzilgan JSON |
| `InvalidOperationException` | Bo'sh javob / `max_tokens` chegarasi |

`stop_reason == "refusal"` bo'lsa (model so'rovni rad etsa) tushunarli xabar beriladi.

---

## 6. Ishga tushirish

### 6.1 Talablar

- **.NET SDK 9.0** yoki undan yuqori — [yuklab olish](https://dotnet.microsoft.com/download)
- Anthropic API kaliti — [console.anthropic.com](https://console.anthropic.com)

Tekshirish:

```bash
dotnet --version
```

### 6.2 AI provayderini tanlash va kalit olish

#### Variant 1 — Google Gemini *(standart, tavsiya etiladi)*

Bepul tarif, **bank kartasi talab qilinmaydi**.

1. [aistudio.google.com/apikey](https://aistudio.google.com/apikey) ga kiring
2. Google hisobingiz bilan kiring → **Create API key**
3. Kalitni nusxa oling

`appsettings.json` allaqachon Gemini uchun sozlangan, faqat kalitni qo'ying:

```bash
dotnet user-secrets set "Ai:ApiKey" "KALITINGIZ"
```

> ⚠️ **Model nomi haqida.** `Model` sifatida `gemini-flash-latest` ishlatilgan —
> bu alias har doim joriy versiyaga ishora qiladi. Aniq versiya nomini
> (`gemini-2.5-flash` kabi) yozmang: Google eski versiyalarni yangi
> foydalanuvchilar uchun yopib qo'yadi va ilova `404` xatosi beradi.
>
> Kalitingizga qaysi modellar ochiqligini ko'rish uchun:
>
> ```bash
> curl -H "Authorization: Bearer KALITINGIZ" https://generativelanguage.googleapis.com/v1beta/openai/models
> ```

#### Variant 2 — Groq *(eng tez)*

1. [console.groq.com/keys](https://console.groq.com/keys) → **Create API Key**

```jsonc
"Ai": {
  "Provider": "OpenAiCompatible",
  "BaseUrl": "https://api.groq.com/openai/v1/",
  "Model": "llama-3.3-70b-versatile",
  "RequestsPerMinute": 25
}
```

#### Variant 3 — OpenRouter *(bir nechta bepul model)*

1. [openrouter.ai/keys](https://openrouter.ai/keys) → kalit yarating
2. Model nomi `:free` bilan tugashi kerak

```jsonc
"Ai": {
  "BaseUrl": "https://openrouter.ai/api/v1/",
  "Model": "meta-llama/llama-3.3-70b-instruct:free",
  "RequestsPerMinute": 10
}
```

#### Variant 4 — Ollama *(lokal, internetsiz, mutlaqo bepul)*

Kuchli kompyuter kerak (kamida 16 GB RAM, yaxshisi NVIDIA GPU).
[ollama.com](https://ollama.com) dan o'rnating, so'ng:

```bash
ollama pull qwen2.5:7b
```

```jsonc
"Ai": {
  "BaseUrl": "http://localhost:11434/v1/",
  "Model": "qwen2.5:7b",
  "ApiKey": "",
  "RequestsPerMinute": 0,
  "BatchSize": 10
}
```

> ⚠️ Zaif protsessorda (masalan Intel Pentium/Celeron, GPU'siz) lokal model
> sekundiga 2-4 token beradi — 1000 qatorli fayl uchun bir necha soat ketadi.
> Bunday holatda 1-3 variantlardan foydalaning.

#### Variant 5 — Anthropic Claude *(pullik, eng yuqori sifat)*

[console.anthropic.com](https://console.anthropic.com) → Billing → kredit
qo'shing → API keys.

```jsonc
"Ai": {
  "Provider": "Anthropic",
  "Model": "claude-opus-5",
  "Effort": "medium",
  "BatchSize": 25,
  "MaxParallelBatches": 3
}
```

Kalitni `ANTHROPIC_API_KEY` muhit o'zgaruvchisiga qo'ying — SDK uni o'zi topadi:

```bash
setx ANTHROPIC_API_KEY "sk-ant-api03-..."
```

#### Kalitni qayerga saqlash

| Usul | Buyruq | Qachon |
|---|---|---|
| User Secrets *(tavsiya)* | `dotnet user-secrets set "Ai:ApiKey" "..."` | Ishlab chiqish — kalit git'ga tushmaydi |
| Muhit o'zgaruvchisi | `setx Ai__ApiKey "..."` | Server / doimiy ishlash (ikkita pastki chiziq!) |
| `appsettings.json` | `"ApiKey": "..."` | Faqat tez sinov uchun. Commit qilmang! |

Birinchi marta User Secrets ishlatishdan oldin:

```bash
dotnet user-secrets init
```

### 6.3 Ishga tushirish

```bash
cd ExcelAiCategorizer
dotnet restore
dotnet run
```

Terminalda ko'rsatilgan manzilni oching (odatda `https://localhost:7xxx`).

Boshqa portda ishga tushirish:

```bash
dotnet run --urls "http://localhost:5000"
```

### 6.4 Foydalanish

1. **Excel fayl** — `.xlsx` tanlang. Sarlavha qatori avtomatik topiladi, shuning
   uchun yuqorisida banner/metama'lumot bo'lgan eksport fayllari ham ishlaydi.
2. **Ustun(lar)** — bu maydonga **ustun nomini** yozing, buyruq emas:
   `Izoh` (bitta), `Qiziqish, Yo'nalish` (bir nechta) yoki `*` (hammasi).
   Bo'sh qoldirsangiz avtomatik tanlanadi.
3. **Kategoriyalar** — har birini yangi qatorda yoki vergul bilan. Bo'sh
   qoldirsangiz AI o'zi aniqlaydi.
4. **Yangi kategoriya ruxsati** — belgilanmasa, model faqat sizning
   ro'yxatingizdan foydalanadi.
5. **Kontekst** — masalan `onlayn do'kon mijozlarining izohlari`.
   Bir jumla ham aniqlikni sezilarli oshiradi.
6. **Tahlilni boshlash** → progress sahifasi ochiladi → tugagach yuklab oling.

### 6.5 Publish (ishlab chiqarishga)

```bash
dotnet publish -c Release -o ./publish
cd publish
dotnet ExcelAiCategorizer.dll
```

`App_Data` papkasiga yozish huquqi borligiga ishonch hosil qiling.

---

## 7. Sozlamalar (`appsettings.json`)

### `Ai` bo'limi

| Kalit | Standart | Tavsif |
|---|---|---|
| `Provider` | `OpenAiCompatible` | `OpenAiCompatible` (bepul provayderlar) yoki `Anthropic` (pullik SDK) |
| `BaseUrl` | Gemini manzili | OpenAI-mos provayder manzili. `Anthropic` uchun e'tiborga olinmaydi |
| `ApiKey` | `""` | API kaliti. Bo'sh bo'lsa: Anthropic → `ANTHROPIC_API_KEY`, Ollama → kalit kerak emas |
| `Model` | `gemini-flash-latest` | Model ID (provayderga bog'liq). Gemini'da alias ishlating — versiya nomi eskiradi |
| `ResponseFormat` | `json_object` | `json_object` (ko'pchilik), `json_schema` (qat'iy sxema), `none` (faqat prompt) |
| `RequestsPerMinute` | `12` | Bepul limitga urilmaslik uchun so'rovlar orasidagi pauza. `0` — cheklovsiz |
| `Effort` | `medium` | Fikrlash chuqurligi — **faqat Anthropic**: `low`…`max` |
| `MaxTokens` | `8000` | Bitta javobdagi maksimal token. `BatchSize` oshirilsa buni ham oshiring |
| `BatchSize` | `20` | Bitta so'rovdagi qatorlar soni. Bepul modellarda 10-20 ishonchliroq |
| `MaxParallelBatches` | `1` | Bir vaqtdagi parallel so'rovlar. Bepul tarifda `1` da qoldiring |
| `MaxRetries` | `3` | Qayta urinishlar soni |
| `RequestTimeoutSeconds` | `300` | Bitta so'rov timeout'i |

**Tayyor konfiguratsiyalar:**

| Provayder | `BaseUrl` | `Model` namunasi | `RequestsPerMinute` |
|---|---|---|---|
| Google Gemini | `https://generativelanguage.googleapis.com/v1beta/openai/` | `gemini-flash-latest` | `12` |
| Groq | `https://api.groq.com/openai/v1/` | `llama-3.3-70b-versatile` | `25` |
| OpenRouter | `https://openrouter.ai/api/v1/` | `...:free` bilan tugaydigan | `10` |
| Ollama (lokal) | `http://localhost:11434/v1/` | `qwen2.5:7b` | `0` |
| Mistral | `https://api.mistral.ai/v1/` | `mistral-small-latest` | `10` |

> Model nomlari va bepul limitlar vaqt o'tishi bilan o'zgaradi —
> aniq ro'yxatni provayderning konsolidan tekshiring.

### `Upload` bo'limi

| Kalit | Standart | Tavsif |
|---|---|---|
| `MaxFileSizeMb` | `25` | Maksimal fayl hajmi (MB) |
| `StorageRoot` | `App_Data` | Vaqtinchalik fayllar papkasi |
| `ResultRetentionHours` | `6` | Natijalar necha soat saqlanadi |

### Model tanlash bo'yicha maslahat

| Vazifa | Tavsiya |
|---|---|
| Murakkab, nozik farqli kategoriyalar | `claude-opus-5` + `Effort: high` |
| Odatdagi kategoriyalash | `claude-opus-5` + `Effort: medium` *(standart)* |
| Katta hajm, oddiy kategoriyalar | `claude-sonnet-5` + `Effort: low` |
| Juda sodda, tezlik muhim | `claude-haiku-4-5` |

---

## 8. Chiqish fayl formati

**`Natija` varag'i:**

| ID | Izoh | Sana | AI kategoriya | Ishonch | Izoh |
|---|---|---|---|---|---|
| 1 | Yetkazib berish sekin | 2026-07-01 | Yetkazib berish | 94% | Kechikish haqida shikoyat |
| 2 | Sifat zo'r, rahmat! | 2026-07-02 | Maqtov | 98% | Ijobiy fikr bildirilgan |
| 3 | Ilova ochilmayapti | 2026-07-03 | Texnik muammo | 45% 🟡 | Ilova ishlamayotgani haqida |

- 🟡 Sariq — ishonch 60% dan past, **qo'lda tekshirish tavsiya etiladi**
- 🔴 Qizil "Tahlil qilinmadi" — bu qator uchun so'rov muvaffaqiyatsiz tugagan

**`Xulosa` varag'i:** manba varaq nomi, tahlil qilingan ustun, jami qatorlar,
sana va kategoriyalar taqsimoti (soni + foizi).

---

## 9. Xatoliklar va yechimlar

| Xato | Sabab | Yechim |
|---|---|---|
| `AI provayderi 401 qaytardi` | Kalit noto'g'ri yoki qo'yilmagan | `dotnet user-secrets set "Ai:ApiKey" "..."` |
| `AI provayderi 404 qaytardi` | Model nomi noto'g'ri | Provayder konsolidan mavjud model nomini oling |
| `AI provayderi 429 qaytardi` | Bepul limit tugadi | Kunlik limit bo'lsa ertaga davom eting yoki boshqa provayderga o'ting |
| `Ai:BaseUrl sozlanmagan` | `BaseUrl` bo'sh | 7-bo'limdagi jadvaldan manzilni ko'chiring |
| `Javobni JSON sifatida o'qib bo'lmadi` | Model yomon javob berdi | `BatchSize` ni kamaytiring yoki kuchliroq model tanlang |
| Ollama'da `Connection refused` | Ollama ishlamayapti | `ollama serve` ni ishga tushiring |
| `Could not resolve API key` | Anthropic kaliti topilmadi | 6.2-bo'lim, 5-variant; `setx` dan keyin terminalni qayta oching |
| `'X' ustuni topilmadi` | Ustun nomida xato | Xato xabarida mavjud ustunlar ro'yxati beriladi |
| `Faylda sarlavhadan tashqari ma'lumot yo'q` | Faqat sarlavha bor | Faylni tekshiring |
| `Faqat .xlsx qo'llab-quvvatlanadi` | `.xls` yoki `.csv` yuklandi | Excel'da "Save As → .xlsx" qiling |
| `429` xatolari ko'p | Rate limit | `MaxParallelBatches` ni `1`–`2` ga tushiring |
| `Javob token chegarasiga yetdi` | To'da juda katta | `BatchSize` ni kamaytiring yoki `MaxTokens` ni oshiring |
| Ko'p qator "Tahlil qilinmadi" | To'dalar qulagan | Loglarni tekshiring; `BatchSize` ni kamaytiring |
| Natija 404 | Muddati o'tgan | `ResultRetentionHours` ni oshiring |

Loglar konsolda ko'rinadi. Batafsil log uchun `appsettings.Development.json`:

```json
"Logging": { "LogLevel": { "ExcelAiCategorizer": "Debug" } }
```

---

## 10. Xarajat va unumdorlik

### Bepul tarif (standart sozlama)

**Pul ketmaydi.** Cheklov — vaqt va kunlik so'rovlar soni.
`BatchSize: 20` va `RequestsPerMinute: 12` bilan:

| Qatorlar | So'rovlar | Taxminiy vaqt | Narx |
|---|---|---|---|
| 100 | 5 | ~30 soniya | $0 |
| 1 000 | 50 | ~5 daqiqa | $0 |
| 10 000 | 500 | ~45 daqiqa | $0 |

Bepul tariflarda odatda **kunlik so'rovlar chegarasi** ham bo'ladi. Agar
`429` xatolari boshlansa yoki kunlik limit tugasa — ertasi kuni davom eting,
yoki boshqa provayderga o'ting (Gemini ↔ Groq ↔ OpenRouter).

**Bepul limitni tejash:**

1. `BatchSize` ni oshiring (`30`) — kamroq so'rov ketadi. `MaxTokens` ni ham
   oshiring (`12000`), aks holda javob kesiladi.
2. Faqat kerakli ustunni tahlil qiling — takroriy qatorlarni oldindan filtrlang.
3. Kontekstni aniq yozing — model kamroq adashadi, qayta urinish kam bo'ladi.

### Pullik tarif (`Provider: "Anthropic"`)

`claude-opus-5`, $5/$25 per MTok, o'rtacha 100 belgili matn:

| Qatorlar | So'rovlar | Taxminiy narx | Taxminiy vaqt |
|---|---|---|---|
| 100 | 4 | ~$0.02 | ~15 soniya |
| 1 000 | 40 | ~$0.20 | ~2 daqiqa |
| 10 000 | 400 | ~$2.00 | ~20 daqiqa |

Arzonlashtirish: `claude-sonnet-5` (~40% arzon), `Effort: "low"`,
kattaroq `BatchSize`.

**Tezlashtirish:** `MaxParallelBatches` ni oshiring — lekin rate limit'ni
hisobga oling (429 xatolari boshlansa kamaytiring).

---

## 11. Kengaytirish yo'nalishlari

| Vazifa | Nima qilish kerak |
|---|---|
| **Vazifalar davomiyligi** | `InMemoryJobStore` o'rniga EF Core / Redis. Interfeys allaqachon ajratilgan |
| **Bir nechta server** | `ChannelJobQueue` o'rniga RabbitMQ / Azure Service Bus |
| **Ustun tanlash oldindan** | Yuklashdan keyin sarlavhalarni ko'rsatib, dropdown'dan tanlatish |
| **`.csv` qo'llab-quvvatlash** | `IExcelService` ga `CsvService` implementatsiyasini qo'shish |
| **Bir nechta ustun** | `ExcelRowItem.Text` ni bir necha ustundan birlashtirib yasash |
| **Batch API** | Anthropic Message Batches API — 50% arzon, lekin sekinroq (24 soatgacha) |
| **Foydalanuvchi hisoblari** | ASP.NET Core Identity + har bir job'ga `UserId` |
| **Diagramma** | Xulosa varag'iga ClosedXML chart yoki progress sahifasiga Chart.js |

---

## Litsenziya va bog'liqliklar

| Paket | Versiya | Vazifa |
|---|---|---|
| `ClosedXML` | 0.105.1 | Excel `.xlsx` o'qish/yozish (MIT) |
| `Anthropic` | 12.39.0 | Rasmiy Claude SDK — faqat `Provider: "Anthropic"` uchun |
| ASP.NET Core | 9.0 | Veb-freymvork |

Bepul provayderlar uchun qo'shimcha paket **kerak emas** — `HttpClient` va
`System.Text.Json` freymvorkning o'zida bor.

### Maxfiylik haqida ogohlantirish

Bepul tariflarda provayderlar odatda yuborilgan ma'lumotni modelni
yaxshilash uchun ishlatish huquqini saqlab qoladi. Agar Excel faylingizda
**shaxsiy yoki maxfiy ma'lumot** bo'lsa (mijozlar ismi, telefon raqami,
shartnoma tafsilotlari) — bepul bulutli tarifni ishlatmang. Bunday holda:

- Ma'lumotni oldindan anonimlashtiring, yoki
- Lokal Ollama'dan foydalaning (ma'lumot kompyuteringizdan chiqmaydi), yoki
- Pullik tarifga o'ting (odatda ma'lumot o'qitish uchun ishlatilmaydi).

`App_Data/` papkasini `.gitignore` ga qo'shishni unutmang:

```gitignore
App_Data/
appsettings.Development.json
```
