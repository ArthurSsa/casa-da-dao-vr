# Casa da DAO · VR para Meta Quest

Experiência em realidade virtual do hackathon **Web 3.0 em VR** (Desafio 05, DAO, ampliado). Três cômodos de uma casa antiga, ligados pelas próprias portas e escadas. Em cada sala o visitante vota numa proposta e a casa obedece: ele encolhe ou cresce, a cozinha perde a gravidade, o sótão muda de clima. Ninguém manda na casa, só os votos.

```
Sala de Estar --porta--> Cozinha --arco--> Sótão --escada desce--> Sala de Estar
```

| Sala | Proposta | Opção A | Opção B |
| --- | --- | --- | --- |
| Sala de Estar | Tamanho | Minúsculo | Gigante |
| Cozinha | Física | Gravidade zero | Gravidade lunar |
| Sótão | Clima | Pôr do sol | Noite azul |

Cada voto entra num **livro-razão** com hash de transação (simulado), é imutável (1 carteira = 1 voto), membros simulados votam em seguida e o resultado se executa sozinho, como um smart contract. Com as 3 propostas executadas aparece “Desafio concluído”.

## Tecnologia

- **Unity 6000.3.9f1** (Unity 6.3 LTS) com módulo Android
- **Meta XR SDK v209** (Core + Interaction SDK): `OVRCameraRig` + `OVRComprehensiveInteractionRig` (mãos, controles, Poke, Grab, teleporte)
- **OpenXR + Meta XR Feature** (Quest 2, 3 e 3S), perfis Touch / Touch Plus / Touch Pro
- **URP** sem sombras em tempo real, MSAA 4x
- Modelos `.glb` importados com **glTFast**

## Como abrir

1. Clone o repositório e abra a pasta no **Unity Hub** (Add > Add project from disk) com o Unity **6000.3.9f1**. A primeira abertura importa os pacotes e demora alguns minutos.
2. Abra a cena pelo menu **DAO > Abrir cena principal** (`Assets/DAO/Scenes/DAO_3Salas.unity`).

## Testar sem óculos (mouse e teclado)

Dê **Play** sem headset conectado: o projeto troca o rig VR por uma câmera de primeira pessoa (se não trocar, aperte **F1**).

| Ação | Tecla |
| --- | --- |
| Andar / correr | WASD ou setas / Shift |
| Olhar | botão direito + mouse |
| Votar | clique esquerdo no botão do pedestal, ou Z / X |
| Trocar de sala | parar dentro do círculo de luz verde (porta, arco, escada) |
| Atalhos | 1/2/3 ir para a sala · R reabrir votação · T +1 token · 0 escala normal · H esconder ajuda |

Erros de “OpenXR runtime” no Console são esperados sem óculos.

## Rodar no Quest

- **Pelo PC (Link):** instale o Meta Horizon Link, defina-o como *Runtime OpenXR ativo* nas configurações, conecte o Quest por USB-C e dê Play.
- **APK:** menu **DAO > Gerar APK para o Quest** (sai em `Builds/CasaDaDAO.apk`) e instale com o Meta Quest Developer Hub ou `adb install -r Builds/CasaDaDAO.apk`. O Quest precisa estar em modo desenvolvedor.

## Menus do projeto

| Menu | O que faz |
| --- | --- |
| DAO > Configurar projeto para Quest (Meta XR) | Android, OpenXR + Meta XR Feature, URP e o “Fix All” do Meta Project Setup Tool |
| DAO > Montar cena das 3 salas | Recria a cena do zero: salas, portais nas saídas reais, votação, rig da Meta (apaga ajustes manuais) |
| DAO > Abrir cena principal | Abre `DAO_3Salas` |
| DAO > Gerar APK para o Quest | Gera `Builds/CasaDaDAO.apk` |

## Estrutura

```
Assets/DAO/Scripts/Runtime   lógica (DaoRoomManager, DaoPortal, DaoProposal, DaoLedger, DaoPlayerScale, DaoGravity, DaoRoomTint, DaoDesktopMode…)
Assets/DAO/Scripts/Editor    montagem da cena, configuração do Quest e build
Assets/DAO/Models            os 3 ambientes (já corrigidos pelo montador da cena)
Assets/DAO/Scenes            DAO_3Salas.unity
Assets/DAOTests              testes de Play Mode (Window > General > Test Runner)
Docs/Guia                    guia da equipe (abra GUIA_DAO_VR.html no navegador)
```

## Créditos

- “Old living room” e “Old attic” por **Katydid**, [Sketchfab](https://sketchfab.com/Katydid.), licença Sketchfab Standard
- “Cartoon Kitchen Interior”, Sketchfab
- Meta XR SDK (Oculus SDK License)

Projeto acadêmico do curso de Web 3.0 · 2026.
