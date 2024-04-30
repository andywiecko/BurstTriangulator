# Documentation

1. Place `.dll` files for `Unity`, `Burst`, `Collections`, etc. in the `dlls/` folder.
2. Navigate to the `Documentation~/` folder.
3. Build documentation using `docfx`. Optionally, if `make` is available, run `make serve` to build and open documentation in the browser.

> [!NOTE]  
> `unity-xrefmap.yml` contains cross-references between packages. This file is filled manually since there are only a few references to external definitions in the public API.
