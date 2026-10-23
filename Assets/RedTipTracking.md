# Ataques com uma caneta de ponta vermelha

A SampleScene usa Vuforia Engine 11.4.4 para os cartoes e para obter a imagem da camera usada pelo RedTipAttackTracker. Os componentes estao no objeto Vuforia Arena. Nao e necessario instalar OpenCV nem abrir uma segunda camera.

## Configurar Vuforia

1. Deixar o Unity importar o pacote local `Packages/com.ptc.vuforia.engine-11.4.4.tgz`. Se a cena estava aberta durante a migracao, reabrir a SampleScene sem guardar a versao antiga em memoria.
2. Abrir **Arena > Configurar Vuforia** e preencher **App License Key** com a chave da tua conta Vuforia. A chave fica na configuracao local do Vuforia; nao colocar a chave neste documento.
3. O componente **Vuforia Arena Setup** carrega os quatro Image Targets da base de dados `Assets/StreamingAssets/Vuforia/arena.xml`: sword, axe, player1 e player2. Manter `arena.dat` na mesma pasta. **Database Path** e relativo a StreamingAssets: `Vuforia/arena.xml`.
4. A base de dados define os cartoes com largura de **0.063 metros (6.3 cm)** e altura aproximada de **8.798 cm**. A largura da imagem impressa, sem margens de papel, deve corresponder a esse valor. Para outro tamanho, atualizar a base de dados no Vuforia Target Manager e voltar a importar o XML e DAT.
5. A configuracao permite quatro targets simultaneos e usa AR_CONTROLLER para cartoes em movimento. ARCore e o Device Pose Tracker estao desativados. O arranque da camera Vuforia e feito pelo VuforiaArenaSetup.

O SDK foi extraido do instalador oficial encontrado em Downloads. As dependencias diretas de AR Foundation, ARCore e Mock HMD foram removidas do manifest; o Package Manager atualiza o lock na proxima resolucao. Os antigos assets XR, a biblioteca e o ambiente de simulacao foram preservados em `MigrationBackup/ARFoundation`, fora de Assets. A deteccao no telemovel ainda precisa de validacao; a troca de SDK nao garante por si so mais estabilidade.

## Utilizar

1. Executar a aplicacao num telemovel suportado pelo Vuforia, com permissao de camera.
2. Aproximar o cartao da arma ao do jogador ate concluir o equipamento.
3. Manter o cartao do jogador visivel. O cartao da arma pode sair da imagem depois de equipada.
4. Por defeito a caneta controla o Jogador 1. Usar **Trocar jogador** para controlar o Jogador 2.
5. Mostrar uma ponta vermelha suficientemente grande na imagem, com boa luz. O quadrado verde deve acompanhar a ponta.
6. Fazer um movimento curto e decidido para cima, baixo, esquerda ou direita. As direcoes referem-se ao ecra do telemovel, nao aos eixos 3D do personagem.

O painel mostra a deteccao e o ultimo ataque reconhecido, mesmo sem clips de ataque. Cada gesto exige aproximadamente 12% do lado menor do ecra em 0.45 segundos; existe uma pausa de 0.65 segundos entre ataques. Gestos diagonais sem um eixo dominante sao ignorados. Perder a ponta reinicia o gesto, evitando ligar duas deteccoes separadas.

## Colocar as animacoes quando estiverem disponiveis

Abrir `Assets/Animations/KnightIdle.controller` no Animator. Os oito estados, triggers e transicoes ja estao preparados:

| Movimento | Espada | Machado |
| --- | --- | --- |
| Cima | SwordAttackUp | AxeAttackUp |
| Baixo | SwordAttackDown | AxeAttackDown |
| Esquerda | SwordAttackLeft | AxeAttackLeft |
| Direita | SwordAttackRight | AxeAttackRight |

Selecionar cada estado e substituir o campo **Motion** pelo clip correspondente. Os estados usam Idle como placeholder enquanto nao ha clips de ataque. O equipamento escolhe automaticamente o prefixo da arma. Cada estado regressa a Idle no fim; ajustar Exit Time e a duracao da transicao se necessario. Os clips devem ter rig Humanoid compativel com o personagem e Loop Time desligado. O movimento de raiz esta desativado para o personagem continuar sobre o cartao.

`PlayerAvatar.attacked` e o evento para ligar efeitos, som ou logica de combate no futuro. Este trabalho reconhece comandos e dispara o Animator; nao implementa dano nem acertos.

## Afinar no Inspector

Selecionar **Vuforia Arena > Red Tip Attack Tracker** fora do Play Mode para guardar os ajustes.

- **Minimum Saturation / Minimum Brightness / Red Hue Tolerance**: tolerancia a cor e a iluminacao.
- **Minimum Blob Pixels**: reduzir se a ponta for pequena; aumentar para rejeitar ruido.
- **Maximum Blob Fraction**: rejeita grandes zonas vermelhas.
- **Gesture Distance / Gesture Window**: distancia e tempo para reconhecer um gesto.
- **Attack Cooldown**: intervalo minimo entre ataques; adaptar a duracao dos futuros clips.
- **Axis Dominance**: aumentar para exigir gestos mais horizontais ou verticais.
- **Camera Quarter Turns**: rotacao adicional da imagem em passos de 90 graus para calibracao no dispositivo.
- **Flip Camera Y**: ajuste de orientacao caso o indicador apareca espelhado verticalmente no dispositivo. Verificar sempre as quatro direcoes depois de rodar o telemovel.

O detetor escolhe a maior mancha vermelha aceite; outros objetos vermelhos podem interferir. Usar uma ponta ou tampa vermelha visivel e um fundo sem vermelho. Manter o telemovel relativamente parado: mover a camera tambem desloca a ponta no ecra. Este e tracking 2D por cor, sem estimativa da profundidade nem da orientacao da caneta.

## Testar no Editor com Vuforia

Usar o modo Webcam na configuracao do Vuforia e uma webcam disponivel. Mostrar os cartoes impressos e equipar uma arma aproximando-a do jogador. Mostrar a ponta vermelha e confirmar o quadrado verde e as quatro direcoes. O XR Simulation de AR Foundation deixou de fazer parte da cena.

Os estados de ataque usam Idle enquanto nao houver clips. Verificar o painel e o estado ativo no Animator. A orientacao da imagem RGB pode diferir entre webcam e telemovel: calibrar Camera Quarter Turns e Flip Camera Y no dispositivo, se necessario, e testar em portrait e landscape. A leitura respeita o stride da imagem RGB e aplica uma correcao de proporcao para o enquadramento do ecra.

## Verificacao no dispositivo

- Sem arma ou com o cartao do jogador perdido: nao deve disparar ataques.
- Ponta parada: nao deve disparar ataques.
- Quatro movimentos: confirmar o quadrado sobre a ponta e o nome correto no painel.
- Tapar e voltar a mostrar a ponta: nao deve criar um ataque pela deslocacao durante a perda.
- Trocar jogador e trocar arma: confirmar o destinatario e o estado do Animator.
- Repetir em portrait e landscape e ajustar os limites com a iluminacao real.

A compilacao C# foi verificada com as referencias locais do Unity. A camera, a orientacao e os limiares precisam de validacao num dispositivo fisico.
