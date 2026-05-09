# Resumo da Documentacao OpenFOAM para DisperSim 3D

## Contexto
O DisperSim 3D utiliza extensivamente o OpenFOAM para simulacao de dispersao atmosferica. Este resumo consolida a documentacao oficial do OpenFOAM, mapeando o que ja esta implementado e o que pode ser expandido.

**Versao do OpenFOAM utilizada: v2512 (ESI/openfoam.com)**

---

## 1. Solvers Disponiveis no OpenFOAM

### Ja implementados no DisperSim 3D:
| Solver | Tipo | Descricao | Campo escalar |
|--------|------|-----------|---------------|
| **scalarTransportFoam** | Transiente/Steady | Transporte de escalar passivo (solver principal do app) | `T` (direto) |
| **simpleFoam** | Steady-state | Algoritmo SIMPLE para escoamento incompressivel | `T` (direto) |
| **pimpleFoam** | Transiente | PIMPLE (PISO+SIMPLE) para escoamento incompressivel turbulento | `T` (via scalarTransport FO) |
| **buoyantPimpleFoam** | Transiente | Escoamento compressivel com empuxo e transferencia de calor | `s` (via scalarTransport FO) |
| **reactingFoam** | Transiente | Multi-especie compressivel (CH4, O2, N2) | `CH4` (especie nativa) |
| **rhoSimpleFoam** | Steady-state | SIMPLE compressivel com escalar passivo | `s` (via scalarTransport FO) |

### Arquitetura de transporte de escalar por solver:

Os solvers se dividem em dois grupos quanto ao transporte do escalar de concentracao:

**Grupo 1 - Escalar resolvido diretamente pelo solver:**
- `scalarTransportFoam`, `simpleFoam`: O solver resolve a equacao de transporte para `T` nativamente. O termo fonte em `constant/fvOptions` e aplicado durante o solve matricial.

**Grupo 2 - Escalar resolvido via function object `scalarTransport`:**
- `pimpleFoam`: Usa FO para transportar `T` (incompressivel, sem conflito de nomes)
- `buoyantPimpleFoam`: Usa FO para transportar `s` (campo `T` reservado para temperatura termodinamica)
- `rhoSimpleFoam`: Usa FO para transportar `s` (idem)

**Grupo 3 - Escalar como especie quimica nativa:**
- `reactingFoam`: Transporta `CH4` como especie do sistema multi-componente

### IMPORTANTE - fvOptions em solvers compressiveis:

Para solvers do Grupo 2 compressiveis (buoyantPimpleFoam, rhoSimpleFoam), o termo fonte **NAO deve** ser colocado em `constant/fvOptions`. O motivo:
- O `constant/fvOptions` e lido pelo solver principal E pelo scalarTransport FO
- O solver principal nao resolve `s`, mas pode aplicar o source de forma espuria ao campo
- Isso causa acumulo de concentracao sem transporte, resultando em valores irreais e artefatos visuais (pontos dispersos)

**Solucao correta:** Definir o fvOptions **INLINE** dentro do bloco do scalarTransport function object no `controlDict`. Assim, o source so e aplicado pela equacao de transporte do FO.

### Solvers adicionais relevantes para dispersao (nao implementados):
| Solver | Tipo | Potencial uso |
|--------|------|---------------|
| **pisoFoam** | Transiente | Alternativa ao pimpleFoam com PISO puro, mais rapido por timestep |
| **buoyantSimpleFoam** | Steady-state | Versao steady do buoyantPimpleFoam |
| **fireFoam** | Transiente | Incendios e dispersao de fumaca com radiacao |
| **sprayFoam** | Transiente | Sprays e goticulas (dispersao de aerossois) |
| **rhoPimpleFoam** | Transiente | Compressivel transiente (alternativa ao rhoSimpleFoam) |

---

## 2. Geracao de Malha

### blockMesh (ja implementado)
- Decomposicao do dominio em blocos hexaedricos
- Arestas podem ser retas, arcos ou splines
- Grading uniforme ou nao-uniforme para refinamento direcional
- Patches definidos como faces dos blocos

### snappyHexMesh (potencial expansao)
- Gera malhas 3D com hexaedros e split-hexaedros automaticamente
- Aceita geometrias STL como entrada
- Tres fases: castellatedMesh (refinamento), snap (morfismo a superficie), addLayers (camadas prismaticas)
- Controles de qualidade: maxNonOrtho (~65 graus), maxBoundarySkewness (~20), maxInternalSkewness (~4)
- Ideal para modelar obstaculos complexos (edificios, terreno)

### Refinamento local (ja implementado parcialmente)
- topoSet + refineMesh para refinamento em 2 niveis ao redor das fontes
- cellSet baseado em boxToCell/sphereToCell

---

## 3. Esquemas Numericos (fvSchemes)

### Categorias de esquemas:
| Sub-dicionario | Funcao | Opcoes principais |
|----------------|--------|-------------------|
| **ddtSchemes** | Derivadas temporais | `Euler` (1a ordem), `backward` (2a ordem), `CrankNicolson` (2a ordem), `steadyState` |
| **gradSchemes** | Gradientes | `Gauss linear` (padrao), `leastSquares`, `cellLimited Gauss linear` |
| **divSchemes** | Divergencia/Conveccao | `Gauss linear`, `Gauss linearUpwind`, `Gauss upwind`, `Gauss limitedLinear`, `bounded Gauss linearUpwind` |
| **laplacianSchemes** | Laplaciano/Difusao | `Gauss linear corrected`, `Gauss linear uncorrected`, `Gauss linear limited` |
| **snGradSchemes** | Gradiente normal a superficie | `corrected`, `uncorrected`, `limited`, `orthogonal` |
| **interpolationSchemes** | Interpolacao face-centro | `linear`, `upwind`, `linearUpwind` |

### Ja implementados no DisperSim:
- ddtSchemes: `Euler` (transiente) e `steadyState`
- gradSchemes: `Gauss linear`, `cellLimited Gauss linear 1` (buoyant/rhoSimple)
- divSchemes: `Gauss linearUpwind grad(T)`, `Gauss upwind` (para k, epsilon)
- laplacianSchemes: `Gauss linear corrected`
- snGradSchemes: `corrected`

### divSchemes especificos por solver:

| Solver | divSchemes obrigatorios |
|--------|------------------------|
| scalarTransportFoam, simpleFoam | `div(phi,T)`, `div(phi,U)` |
| pimpleFoam | `div(phi,T)`, `div(phi,U)`, `div(phi,k)`, `div(phi,epsilon)`, `div((nuEff*dev2(T(grad(U)))))` |
| buoyantPimpleFoam | `div(phi,U)`, `div(phi,s)`, `div(phi,h)`, `div(phi,K)`, `div(phi,k)`, `div(phi,epsilon)`, `div(((rho*nuEff)*dev2(T(grad(U)))))` |
| reactingFoam | `div(phi,U)`, `div(phi,Yi_h)`, `div(phi,K)`, `div(phi,k)`, `div(phi,epsilon)`, `div(((rho*nuEff)*dev2(T(grad(U)))))` |
| rhoSimpleFoam | `div(phi,U)`, `div(phi,s)`, `div(phi,h)`, `div(phi,K)`, `div(phi,k)`, `div(phi,epsilon)`, `div(((rho*nuEff)*dev2(T(grad(U)))))` |

**ATENCAO:** Solvers compressiveis usam `div(((rho*nuEff)*dev2(T(grad(U)))))` (com `rho*`). Solvers incompressiveis usam `div((nuEff*dev2(T(grad(U)))))` (sem `rho*`). Usar a versao errada causa FOAM FATAL IO ERROR.

### Expansoes possiveis:
- `backward` ou `CrankNicolson 0.5` para 2a ordem temporal (maior precisao)
- `cellLimited Gauss linear 1` para gradientes limitados (estabilidade)
- `Gauss limitedLinear 1` para conveccao limitada TVD

---

## 4. Controle de Solucao (fvSolution)

### Solvers lineares:
| Solver | Tipo de matriz | Uso tipico |
|--------|---------------|------------|
| **GAMG** | Simetrica | Pressao (p, p_rgh) - mais rapido para malhas grandes |
| **PCG** | Simetrica | Pressao (alternativa ao GAMG), densidade (rho) |
| **PBiCGStab** | Assimetrica | Velocidade (U), escalares (T, s, k, epsilon, h) |
| **smoothSolver** | Ambas | Alternativa com smoother (GaussSeidel, symGaussSeidel) |

### Precondicionadores:
- **DILU**: Decomposicao LU incompleta diagonal (assimetricas)
- **DIC**: Decomposicao Cholesky incompleta diagonal (simetricas)
- **FDIC**: DIC mais rapido
- **none**: Sem precondicionador

### Algoritmos de acoplamento pressao-velocidade:
| Algoritmo | Tipo | Parametros chave |
|-----------|------|-----------------|
| **SIMPLE** | Steady | nNonOrthogonalCorrectors, residualControl, relaxationFactors |
| **PISO** | Transiente | nCorrectors (tipico 2-4), nNonOrthogonalCorrectors |
| **PIMPLE** | Transiente | nOuterCorrectors, nCorrectors, nNonOrthogonalCorrectors |

### Campos adicionais por solver em fvSolution:

| Solver | Campos obrigatorios em `solvers {}` |
|--------|-------------------------------------|
| scalarTransportFoam | T |
| simpleFoam | U, p, T, k, epsilon |
| pimpleFoam | U, p, T, TFinal, k, epsilon, (U\|k\|epsilon)Final |
| buoyantPimpleFoam | rho, rhoFinal, p_rgh, p_rghFinal, U, h, s, sFinal, k, epsilon, (U\|h\|k\|epsilon)Final |
| reactingFoam | p, pFinal, U, h, k, epsilon, Yi |
| rhoSimpleFoam | U, p, h, s, k, epsilon |

**ATENCAO:** buoyantPimpleFoam REQUER `rho` e `rhoFinal` em fvSolution/solvers. Omiti-los causa FOAM FATAL IO ERROR: "Entry 'rho' not found in dictionary".

### Ja implementados no DisperSim:
- PBiCGStab com DILU para escalar T/s (tol 1e-8)
- GAMG para pressao (tol 1e-6, smoother GaussSeidel)
- PCG com DIC para rho (tol 1e-7)
- SIMPLE com residualControl e relaxationFactors
- PIMPLE com nOuterCorrectors=2, nCorrectors=2

---

## 5. Condicoes de Contorno

### Basicas (ja implementadas):
| Tipo | Descricao | Uso no DisperSim |
|------|-----------|-----------------|
| **fixedValue** | Valor constante | Velocidade na atmosfera, pressao, temperatura |
| **zeroGradient** | Gradiente normal zero | Escalar no solo, pressao no solo |
| **inletOutlet** | Alterna entre fixedValue (entrada) e zeroGradient (saida) | Escalar na atmosfera |
| **noSlip** | Velocidade zero (parede) | Solo em buoyantPimpleFoam |
| **calculated** | Derivado de outros campos | Pressao em buoyantPimpleFoam, alphat na atmosfera |

### Wall Functions (ja implementadas):
| Tipo | Campo | Uso |
|------|-------|-----|
| **nutkWallFunction** | nut | Viscosidade turbulenta na parede (y+ 30-300) |
| **kqRWallFunction** | k | Energia cinetica turbulenta na parede |
| **epsilonWallFunction** | epsilon | Dissipacao na parede |
| **compressible::alphatWallFunction** | alphat | Difusividade termica turbulenta na parede (buoyantPimpleFoam) |

### Campos adicionais em buoyantPimpleFoam:

| Campo | Tipo | Dimensoes | Descricao |
|-------|------|-----------|-----------|
| **p** | calculated | [1 -1 -2 0 0 0 0] (Pa) | Pressao total (derivada de p_rgh) |
| **p_rgh** | fixedFluxPressure/fixedValue | [1 -1 -2 0 0 0 0] | Pressao menos componente hidrostatica |
| **T** | fixedValue/zeroGradient | [0 0 0 1 0 0 0] (K) | Temperatura termodinamica (NAO usar para concentracao!) |
| **alphat** | compressible::alphatWallFunction/calculated | [1 -1 -1 0 0 0 0] | Difusividade termica turbulenta |
| **s** | inletOutlet/zeroGradient | [0 0 0 0 0 0 0] | Escalar passivo de concentracao |

### Condicoes ABL atmosfericas (potencial expansao):
| Tipo | Descricao |
|------|-----------|
| **atmBoundaryLayerInletVelocity** | Perfil logaritmico de vento (lei de parede atmosferica) |
| **atmBoundaryLayerInletK** | Perfil de k consistente com ABL |
| **atmBoundaryLayerInletEpsilon** | Perfil de epsilon para ABL |
| **atmAlphatkWallFunction** | Funcao de parede termica atmosferica |
| **atmEpsilonWallFunction** | Epsilon na parede para ABL |
| **atmNutkWallFunction** | nut na parede para ABL |
| **atmOmegaWallFunction** | omega na parede para ABL (k-omega) |
| **turbulentInlet** | Inlet com flutuacoes turbulentas |

---

## 6. Modelos de Turbulencia

### RAS (Reynolds-Averaged) - ja implementado k-epsilon:
| Modelo | Equacoes | Indicacao |
|--------|----------|-----------|
| **kEpsilon** | k, epsilon | Uso geral, robusto (atual no DisperSim) |
| **kOmegaSST** | k, omega | Melhor para camadas limite, separacao |
| **realizableKE** | k, epsilon | Melhor para jatos, esteiras, recirculacao |
| **RNGkEpsilon** | k, epsilon | Melhor para escoamentos curvos |
| **LaunderSharmaKE** | k, epsilon | Low-Re, sem wall functions |

### LES (Large Eddy Simulation) - potencial expansao:
| Modelo | Descricao |
|--------|-----------|
| **Smagorinsky** | Modelo subgrid classico |
| **kEqn** | LES com equacao de transporte para k subgrid |
| **dynamicKEqn** | Coeficiente de Smagorinsky dinamico |
| **WALE** | Wall-Adapting Local Eddy-viscosity |

### Configuracao (turbulenceProperties):
```
simulationType  RAS;  // ou LES, laminar
RAS {
    model           kEpsilon;
    turbulence      on;
    printCoeffs     on;
}
```

---

## 7. Propriedades de Transporte

### Incompressivel (transportProperties):
```
transportModel  Newtonian;
nu              1.5e-5;    // viscosidade cinematica [m^2/s]
DT              1e-5;      // difusividade molecular [m^2/s]
```

### Compressivel (thermophysicalProperties):
- **heRhoThermo**: Termodinamica baseada em entalpia e densidade
- **pureMixture**: Mistura pura (ar)
- **perfectGas**: Equacao de estado de gas ideal
- Propriedades: Cp, mu, Pr, molWeight
- Transporte: `const` (constante) ou `sutherland` (dependente de T)
- Termodinamica: `hConst` (constante) ou `janaf` (tabelas JANAF/NASA)

### Gravidade (obrigatorio para buoyantPimpleFoam):
```
dimensions      [0 1 -2 0 0 0 0];
value           (0 0 -9.81);
```
Arquivo: `constant/g`

---

## 8. Termos Fonte (fvOptions)

### Duas estrategias de injecao de fonte:

**Estrategia 1 - fvOptions global (solvers incompressiveis):**

Para solvers que resolvem o escalar diretamente (scalarTransportFoam, simpleFoam), o source fica em `constant/fvOptions`:
```
source_0
{
    type            scalarSemiImplicitSource;
    active          true;

    scalarSemiImplicitSourceCoeffs
    {
        selectionMode   cellSet;
        cellSet         sourceZone_0;
        volumeMode      absolute;
        injectionRateSuSp
        {
            T   (0.5 0);    // (Su explicito, Sp implicito) em kg/s
        }
    }
}
```

**Estrategia 2 - fvOptions inline no scalarTransport FO (solvers compressiveis):**

Para solvers que usam scalarTransport function object (buoyantPimpleFoam, rhoSimpleFoam), o source DEVE ser definido INLINE no bloco do FO no controlDict. Ver secao 9 para sintaxe completa.

**Motivo:** Se o source for colocado em `constant/fvOptions`, ele pode ser aplicado de forma espuria pelo solver principal (que nao resolve para `s`), causando acumulo de concentracao sem transporte e resultados visivelmente incorretos (pontos dispersos em vez de pluma coerente).

### Parametros do scalarSemiImplicitSource:
| Parametro | Valor | Descricao |
|-----------|-------|-----------|
| `selectionMode` | `cellSet`, `all`, `points` | Como selecionar as celulas |
| `cellSet` | nome | Nome do cellSet definido por topoSet |
| `volumeMode` | `absolute`, `specific` | `absolute`: valor total; `specific`: por unidade de volume |
| `injectionRateSuSp` | `{campo (Su Sp)}` | Su = fonte explicita, Sp = coeficiente implicito |

### Outros fvOptions relevantes (potencial expansao):
| Tipo | Uso |
|------|-----|
| **fixedTemperatureConstraint** | Fixar temperatura em regiao |
| **meanVelocityForce** | Forcar velocidade media (canal) |
| **actuationDiskSource** | Disco atuador (turbinas eolicas) |
| **codedSource** | Fonte customizada em C++ |
| **patchMeanVelocityForce** | Forcar vazao em patch |

---

## 9. Function Objects (Pos-processamento)

### scalarTransport (ja implementado para pimpleFoam, buoyantPimpleFoam, rhoSimpleFoam)

O `scalarTransport` function object resolve uma equacao de transporte adicional para um campo escalar passivo, acoplado ao fluxo do solver principal.

#### Configuracao para solver INCOMPRESSIVEL (pimpleFoam):
```
functions
{
    TTransport
    {
        type            scalarTransport;
        libs            ("libsolverFunctionObjects.so");
        field           T;
        nCorr           2;
        writeControl    writeTime;
        D               1e-5;
    }
}
```
Neste caso, o source fica em `constant/fvOptions` (o FO le automaticamente).

#### Configuracao para solver COMPRESSIVEL (buoyantPimpleFoam, rhoSimpleFoam):
```
functions
{
    sTransport
    {
        type            scalarTransport;
        libs            ("libsolverFunctionObjects.so");
        field           s;
        rho             rho;            // OBRIGATORIO para compressivel
        nCorr           2;
        resetOnStartUp  false;
        writeControl    writeTime;
        D               1e-5;

        // Source INLINE - evita conflito com solver principal
        fvOptions
        {
            source_0
            {
                type            scalarSemiImplicitSource;
                active          true;
                scalarSemiImplicitSourceCoeffs
                {
                    selectionMode   cellSet;
                    cellSet         sourceZone_0;
                    volumeMode      absolute;
                    injectionRateSuSp
                    {
                        s       (0.5 0);
                    }
                }
            }
        }
    }
}
```

#### Parametros do scalarTransport FO:

| Parametro | Default | Descricao |
|-----------|---------|-----------|
| `field` | obrigatorio | Nome do campo escalar a transportar |
| `rho` | `none` | Nome do campo de densidade. **Setar para `rho` em solvers compressiveis.** Quando setado, a equacao muda de `ddt(s) + div(phi,s)` para `ddt(rho,s) + div(phi,s)` |
| `phi` | `phi` | Nome do campo de fluxo (geralmente nao precisa alterar) |
| `D` | obrigatorio | Coeficiente de difusao molecular [m^2/s] |
| `nCorr` | `0` | Numero de iteracoes corretivas por timestep. Recomendado: 2 |
| `resetOnStartUp` | `true` | Se `true`, zera o campo no inicio. Setar `false` para manter valores iniciais |
| `writeControl` | `timeStep` | Quando escrever o campo. `writeTime` sincroniza com o solver |
| `fvOptions` | (vazio) | Sub-dicionario com sources inline. **Quando presente, sobrescreve `constant/fvOptions` para este campo** |

#### ATENCAO - `fvOptions true` NAO funciona:
Versoes antigas da documentacao mencionam `fvOptions true;` como flag booleana. Isso causa FOAM FATAL ERROR no v2512:
```
Attempt to return primitive entry 'fvOptions' as a sub-dictionary
```
O `fvOptions` dentro do FO deve ser um **sub-dicionario** (bloco `{ }`), nao um booleano.

#### Conflito de nomes de campos:
- `buoyantPimpleFoam` e `rhoSimpleFoam` ja resolvem `T` como temperatura termodinamica
- O escalar de concentracao DEVE usar um nome diferente (e.g., `s`)
- `pimpleFoam` (incompressivel) nao tem equacao de energia, entao `T` pode ser usado para concentracao
- `reactingFoam` transporta especies nativamente (`CH4`), sem necessidade de scalarTransport FO

### Outros function objects uteis (potencial expansao):
| Tipo | Descricao |
|------|-----------|
| **fieldAverage** | Medias temporais de campos |
| **fieldMinMax** | Min/max de campos |
| **volFieldValue** | Integrais de volume (massa total de poluente) |
| **surfaceFieldValue** | Integrais de superficie (fluxo em plano) |
| **probes** | Sondas pontuais (concentracao em pontos) |
| **sets** | Linhas/planos de amostragem |
| **residuals** | Monitorar residuos |
| **CourantNo** | Monitorar numero de Courant |
| **wallShearStress** | Tensao de cisalhamento na parede |

---

## 10. Execucao Paralela

### decomposePar (ja implementado):
- Metodos: `scotch` (automatico), `simple` (direcional), `hierarchical`
- Configuracao em `system/decomposeParDict`
- reconstructPar para recombinar

### MPI (ja implementado):
- mpirun/mpiexec com -np N
- Suporte a WSL2, Docker, BlueCFD, NativeWindows

---

## 11. Licoes Aprendidas (Troubleshooting)

### Erros comuns encontrados durante o desenvolvimento:

| Erro | Causa | Solucao |
|------|-------|---------|
| `cannot find file processor0/0/p` | buoyantPimpleFoam requer campo `p` (calculado de p_rgh) | Adicionar `WriteBuoyantPField` com type `calculated` |
| `cannot find file processor0/0/T` | buoyantPimpleFoam requer campo `T` (temperatura) | Escrever `0/T` (nao `0/T.air`) com dimensions `[0 0 0 1 0 0 0]` |
| `cannot find file processor0/0/alphat` | k-epsilon + buoyantPimpleFoam requer `alphat` | Adicionar campo `alphat` com `compressible::alphatWallFunction` no ground |
| `Entry 'rho' not found in fvSolution/solvers` | buoyantPimpleFoam precisa resolver `rho` | Adicionar `rho` (PCG+DIC) e `rhoFinal` em fvSolution/solvers |
| `Entry 'div(((rho*nuEff)*dev2(...)))' not found` | Solver compressivel precisa do termo com `rho*` | Usar `div(((rho*nuEff)*dev2(T(grad(U)))))` em vez de `div((nuEff*dev2(T(grad(U)))))` |
| `Attempt to return primitive entry 'fvOptions' as sub-dictionary` | `fvOptions true;` nao e valido no v2512 | Usar sub-dicionario `fvOptions { ... }` inline no FO |
| Pluma com pontos dispersos / concentracao irreal | Source em `constant/fvOptions` para campo `s` nao resolvido pelo solver principal | Mover source para fvOptions INLINE no scalarTransport FO |

### Checklist para novo solver compressivel:
1. Campos obrigatorios em `0/`: U, p, p_rgh, T, k, epsilon, nut, alphat, s (ou nome do escalar)
2. `fvSolution/solvers`: incluir `rho`, `rhoFinal`, `p_rgh`, `p_rghFinal`, `s`, `sFinal`
3. `fvSchemes/divSchemes`: usar `rho*nuEff` (nao `nuEff`), incluir `div(phi,s)`
4. `constant/g`: arquivo de gravidade com `(0 0 -9.81)`
5. `constant/thermophysicalProperties`: heRhoThermo + pureMixture + perfectGas
6. Source do escalar: INLINE no scalarTransport FO, nao em `constant/fvOptions`

---

## 12. Mapeamento: DisperSim 3D vs Documentacao OpenFOAM

### O que JA esta bem implementado:
- 6 solvers para dispersao (scalarTransportFoam, simpleFoam, pimpleFoam, buoyantPimpleFoam, reactingFoam, rhoSimpleFoam)
- Geracao de malha com blockMesh + refinamento local
- Esquemas numericos fundamentais (Euler, linearUpwind, Gauss linear)
- Condicoes de contorno basicas e wall functions k-epsilon
- Termos fonte via fvOptions (direto) e scalarTransport FO (inline)
- Execucao paralela com MPI
- Leitura de resultados e cache de timesteps
- Suporte a campos escalares com nomes diferentes por solver (T, s, CH4)

### Oportunidades de expansao baseadas na documentacao:
1. **snappyHexMesh** para geometrias complexas (edificios STL)
2. **Condicoes ABL atmosfericas** (perfis logaritmicos nativos do OpenFOAM)
3. **k-omega SST** como modelo de turbulencia alternativo
4. **Esquemas de 2a ordem temporal** (backward, CrankNicolson)
5. **Function objects** para pos-processamento (probes, fieldAverage, surfaceFieldValue)
6. **LES** para simulacoes de alta fidelidade

---

## Fontes consultadas:
- [OpenFOAM User Guide - Solvers](https://www.openfoam.com/documentation/user-guide/a-reference/a.1-standard-solvers)
- [OpenFOAM User Guide - Turbulence](https://doc.cfd.direct/openfoam/user-guide-v13/turbulence)
- [OpenFOAM User Guide - fvSchemes](https://doc.cfd.direct/openfoam/user-guide-v13/fvschemes)
- [OpenFOAM User Guide - fvSolution](https://doc.cfd.direct/openfoam/user-guide-v13/fvsolution)
- [OpenFOAM User Guide - Boundary Conditions](https://www.openfoam.com/documentation/user-guide/a-reference/a.4-standard-boundary-conditions)
- [OpenFOAM User Guide - Mesh Generation](https://www.openfoam.com/documentation/user-guide/4-mesh-generation-and-conversion)
- [OpenFOAM User Guide - snappyHexMesh](https://www.openfoam.com/documentation/guides/latest/doc/guide-meshing-snappyhexmesh.html)
- [OpenFOAM - k-epsilon](https://www.openfoam.com/documentation/guides/latest/doc/guide-turbulence-ras-k-epsilon.html)
- [OpenFOAM - k-omega SST](https://www.openfoam.com/documentation/guides/latest/doc/guide-turbulence-ras-k-omega-sst.html)
- [OpenFOAM - fvOptions Semi-implicit source](https://www.openfoam.com/documentation/guides/latest/doc/guide-fvoptions-sources-semi-implicit.html)
- [OpenFOAM - scalarTransport function object](https://www.openfoam.com/documentation/guides/latest/doc/guide-fos-solvers-scalar-transport.html)
- [OpenFOAM v2312 - scalarTransport](https://doc.openfoam.com/2312/tools/post-processing/function-objects/solvers/scalarTransport/)
- [OpenFOAM v2312 - Semi-implicit source](https://doc.openfoam.com/2312/tools/processing/numerics/fvoptions/sources/rtm/semiImplicit/)
- [OpenFOAM - pimpleFoam](https://www.openfoam.com/documentation/guides/latest/doc/guide-applications-solvers-incompressible-pimpleFoam.html)
- [OpenFOAM - buoyantPimpleFoam](https://www.openfoam.com/documentation/guides/latest/doc/guide-applications-solvers-heat-transfer-buoyantPimpleFoam.html)
- [OpenFOAM ABL Boundary Conditions](https://www.openfoam.com/news/main-news/openfoam-v2206/boundary-conditions)
- [Atmospheric Dispersion with OpenFOAM (MDPI)](https://www.mdpi.com/2073-4433/12/8/933)
