# HeaderInheritance Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → `HeaderInheritance`

The header inheritance convention (Appendix K): extensions that share the keywords of the primary HDU.

```csharp
public static class HeaderInheritance
```

## Methods

| Member | Description |
| --- | --- |
| [`GetEffectiveHeader(FitsFile, int)`](#fitsnet-headerinheritance-geteffectiveheader-fitsnet-fitsfile-system-int32) | The header of an HDU as an application should see it. |

## Method details

<a id="fitsnet-headerinheritance-geteffectiveheader-fitsnet-fitsfile-system-int32"></a>
### `GetEffectiveHeader(FitsFile, int)`

The header of an HDU as an application should see it. When the HDU is an extension whose header has `INHERIT = T`, the keywords of the primary header are added after its own, except the mandatory ones and the commentary cards, and except those the extension defines itself (its value takes precedence). The HDU itself is not modified.

```csharp
public static FitsHeader GetEffectiveHeader(this FitsFile file, int index)
```

**Parameters**

- `file` — The file the HDU belongs to.
- `index` — Index of the HDU in `file`.
