# Preprocessor

Triangulation for *non-uniform* data can be demanding, and a few algorithm steps may get stuck if the data is not preprocessed properly.
It is highly recommended that the user prepares the input data on their own; however, this project provides a few built-in methods.

| [`Preprocessor`][preprocessor] | Description        |
|--------------|--------------------|
| None         | Default, no effect. |
| COM          | Transforms input into normalized local space, i.e. [-1, 1] box. |
| [PCA](#pca-transformation) | Transforms input into normalized coordinate systems obtained with *principal component analysis*. |

To use one of the following preprocessors, use the corresponding settings:

```csharp
triangulator.Settings.Preprocessor = Triangulator.Preprocessor.COM;
```

## PCA transformation

This algorithm can help in situations when the Sloan algorithm gets stuck.
The transformation can be applied using the following steps:

1. Calculate com: $\mu = \displaystyle\frac1n\sum_{i=1}^n x_i$.
2. Transform points: $x_i \to x_i -\mu$.
3. Calculate covariance matrix: $\text{cov} = \frac1n\sum_i x_i x_i^{\mathsf T}$.
4. Solve eigenproblem for $\text{cov}$: $\text{cov}u_i =v_i u_i$.
5. Transform points using matrix $U = [u_i]$: $x_i \to U^{\mathsf T} .x_i$.
6. Calculate vector center $c = \frac12[\max(x_i) + \min(x_i)]$ and vector scale $s=2/[\max(x_i) - \min(x_i)]$, where $\min$, $\max$, and "$/$" are component wise operators.
7. Transform points: $x_i \to  s (x_i-c)$, assuming component wise multiplication.

To summarize, the transformation is given by:

$$
\boxed{x_i \to s[U^{\mathsf T}(x_i - \mu) - c]}
$$

and the inverse transformation:

$$
\boxed{x_i \to U(x_i / s + c) + \mu}.
$$

> [!NOTE]  
> The PCA transformation does not preserve the [`RefinementThresholds.Angle`][angle] used for refinement.
> As a result, triangles can be classified as bad in the PCA local space.

[angle]: xref:andywiecko.BurstTriangulator.RefinementThresholds.Angle
[preprocessor]: xref:andywiecko.BurstTriangulator.Preprocessor
