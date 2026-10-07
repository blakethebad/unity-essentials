# Extensions

Extension methods for the types Unity gives you but does not extend. The package is a home for
small, dependency-free helpers; it starts with time formatting.

- **Assembly:** `UnityEssentials.Extensions` (`Runtime/`) — standalone, nothing beyond the BCL.
- **Namespace:** `UnityEssentials.Extensions` — flat; one file per extended type, no subfolders.
- **Tests:** `UnityEssentials.Extensions.Tests` (EditMode only).

```csharp
using UnityEssentials.Extensions;

timerText.SetText(timer.Remaining.ToTimeString(roundUp: true));   // 1:35
```

---

## Layout

One class per extended type — `NumericExtensions` here — named after the type it extends, never
after what it returns: `ToTimeString` returns a string but extends a number, so it lives with the
numbers. The class name never shows up at a call site, so its only job is telling you where the
next extension goes. Tests mirror it as `{Type}Extensions{Concern}Tests`.

---

## Time formatting

`ToTimeString` turns a seconds value into a clock string. It extends `float`, `double`, `int` and
`long`, so it reaches whichever type the clock driving it keeps its time in — every overload
funnels through the same formatting, so a `float` and the `double` beside it always print the same
string.

The layout is chosen by argument rather than by a format enum. **Pass them by name** — that is
what makes the call readable:

```csharp
95.5f.ToTimeString()                        // 1:35
95.5f.ToTimeString(decimals: 1)             // 1:35.5
95.5f.ToTimeString(hours: true)             // 0:01:35
95.5f.ToTimeString(minutes: false)          // 95
3903.25f.ToTimeString()                     // 1:05:03   (hours appear on their own)
3903.25f.ToTimeString(hours: false)         // 65:03     (hours roll into the minutes)
remaining.ToTimeString(roundUp: true)       // a countdown — see below
```

| Argument   | Default | Meaning                                                                        |
| ---------- | ------- | ------------------------------------------------------------------------------ |
| `hours`    | `null`  | `null` shows an hours field only from an hour up, `true` always, `false` never. |
| `minutes`  | `true`  | `false` prints total seconds and ignores `hours`.                               |
| `decimals` | `0`     | Fractional second digits, 0 to 3.                                               |
| `roundUp`  | `false` | Round the smallest displayed unit up instead of truncating.                     |

Formats without an hours field roll the hours into the minutes rather than dropping them, so
nothing is ever lost — `65:03`, not `5:03`.

### Rounding

`roundUp` is the difference between a stopwatch and a countdown:

- **Off** (default) truncates. Elapsed 0.9 s still reads `0:00` — what a stopwatch shows.
- **On** rounds up. Remaining 0.1 s still reads `0:01`, and the display only reaches `0:00` when
  the time is actually spent — what a countdown shows. Use it for `Timer.Remaining`.

It applies at display precision, so it is the tenth or the millisecond that rounds when `decimals`
shows one. A whole-second `int` or `long` has nothing to snap, so it does no work on those
overloads — it stays on them so the call does not change shape when a field does.

---

## Writing into a buffer

`ToTimeChars` formats into a `char[]` you own and returns how many characters it wrote. It
allocates nothing at all — no string, no boxed arguments, no temporaries — so it suits a label
refreshed every frame, or many of them.

```csharp
private readonly char[] _clock = new char[NumericExtensions.MaxTimeStringLength];

private void Update()
{
    var length = timer.Remaining.ToTimeChars(_clock, roundUp: true);
    timerText.SetCharArray(_clock, 0, length);   // TMP consumes the buffer directly
}
```

Create the buffer once and keep it in a field — a buffer allocated per call would defeat the
point. It takes the same arguments as `ToTimeString`, and the two paths share one formatter, so
they can never disagree.

- Size the buffer with `NumericExtensions.MaxTimeStringLength` and no argument can overflow it.
- Only the returned number of characters is written; the rest of the buffer is left untouched.
  Always render `(buffer, 0, length)` — a shorter value does not erase the tail of a longer one.
- A buffer too short for the clock string throws `ArgumentException` rather than writing a
  truncated time. It is a sizing bug, and it fails the same way every call.

Reach for `ToTimeString` everywhere else. A label that changes once a second allocates one small
string per second, which is not worth a buffer field to avoid.

---

## Contract

- A negative value formats as zero, so a countdown that overshoots never prints a minus sign.
- `NaN`, an infinity, `decimals` outside 0 to 3, or a value too large to count in the units being
  printed all throw `ArgumentOutOfRangeException`.
- Binary floating-point noise never inflates a rounded-up value: `0.1f * 10f` is `0:01`, not
  `0:02`, and `0.1 + 0.2` is `0:00.3`, not `0:00.4`.
- Sub-second digits truncate what the type cannot hold: `95.432f` prints `1:35.431`, because that
  float really is 95.43199920654297. Use the `double` overload where the last digit matters.
- The output is culture-invariant — digits are written by hand, so the separators and numerals are
  the same on every device.
