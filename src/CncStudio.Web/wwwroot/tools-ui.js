// Cadastro local de até 20 ferramentas com desenho técnico dinâmico.
// Os códigos de tipo (fresa, fresa-esferica...) são os do modelo do Core (ToolKind); a lista pode ficar vazia.
const KINDS=[['fresa','Fresa de topo'],['fresa-esferica','Fresa esférica'],['fresa-toroidal','Fresa toroidal'],['broca','Broca'],['macho','Macho'],['fresa-facear','Fresa de facear']];
export function mountToolEditor(parent, data) {
  const defaults = () => ({Kind:'fresa',Number:1,Diameter:10,CornerRadius:0,TipAngle:118,CuttingLength:25,HolderDiameter:35,HolderLength:45});
  const tools = Array.isArray(data.Tools) ? data.Tools.map(t => ({...defaults(),...t})).slice(0,20) : [];
  let selected = 0;
  const ui = document.createElement('div');
  ui.style.cssText='display:flex;flex-direction:column;gap:10px';
  parent.append(ui);
  const selector=document.createElement('select');
  selector.setAttribute('aria-label','Selecionar ferramenta');
  selector.style.cssText='width:100%;min-height:46px;background:#161b22;color:#e6edf3;border:1px solid #4a5665;border-radius:7px;padding:8px;font-size:16px';
  const buttons=document.createElement('div');buttons.style.cssText='display:flex;gap:8px;flex-wrap:wrap';
  function btn(label, action) {const b=document.createElement('button');b.type='button';b.textContent=label;b.style.cssText='min-height:44px;padding:8px 12px;background:#27394a;color:white;border:1px solid #526579;border-radius:7px';b.onclick=action;buttons.append(b);return b;}
  const add=btn('+ Adicionar',()=>{if(tools.length>=20)return;let n=1;while(tools.some(t=>t.Number===n))n++;tools.push({...defaults(),Number:n});selected=tools.length-1;render();});
  const remove=btn('Excluir',()=>{if(!tools.length)return;tools.splice(selected,1);selected=Math.max(0,Math.min(selected,tools.length-1));render();});
  const count=document.createElement('div');count.style.cssText='font-size:12px;color:#9baab9';
  const preview=document.createElementNS('http://www.w3.org/2000/svg','svg');preview.setAttribute('viewBox','0 0 340 230');preview.style.cssText='width:100%;max-height:230px;background:#101b26;border:1px solid #30445a;border-radius:9px';
  const fields=document.createElement('div');fields.style.cssText='display:flex;flex-direction:column;gap:8px';
  ui.append(selector,buttons,count,preview,fields);
  selector.onchange=()=>{selected=Number(selector.value);render();};
  const numeric=(label,key,integer=false)=>{
    const row=document.createElement('label');row.style.cssText='display:grid;grid-template-columns:minmax(0,1fr) minmax(115px,1fr);align-items:center;gap:12px';
    const title=document.createElement('span');title.textContent=label;
    const input=document.createElement('input');input.type='number';input.inputMode='decimal';input.step=integer?'1':'any';input.min=integer?'1':'0.001';input.required=true;input.value=String(tools[selected][key]);
    input.style.cssText='width:100%;min-width:0;box-sizing:border-box;background:#161b22;color:white;border:1px solid #4a5665;border-radius:7px;min-height:44px;padding:8px;font-size:17px';
    input.oninput=()=>{tools[selected][key]=input.value===''?NaN:Number(input.value);draw();updateLabels();};
    row.append(title,input);fields.append(row);
  };
  function toolType(){
    const row=document.createElement('label');row.style.cssText='display:grid;grid-template-columns:minmax(0,1fr) minmax(115px,1fr);align-items:center;gap:12px';
    const label=document.createElement('span');label.textContent='Tipo de ferramenta';
    const input=document.createElement('select');input.style.cssText='width:100%;min-width:0;background:#161b22;color:white;border:1px solid #4a5665;border-radius:7px;min-height:44px;padding:8px;font-size:16px';
    for(const [code,label] of KINDS){const option=document.createElement('option');option.value=code;option.textContent=label;input.append(option);}
    input.value=tools[selected].Kind;
    input.onchange=()=>{tools[selected].Kind=input.value;render();};
    row.append(label,input);fields.append(row);
  }
  function updateLabels(){
    count.textContent=tools.length?tools.length+' de 20 ferramentas cadastradas':'Nenhuma ferramenta cadastrada: valem a tabela e os comentários do programa.';
    add.disabled=tools.length>=20;remove.disabled=tools.length===0;selector.disabled=tools.length===0;
    [...selector.options].forEach((opt,i)=>opt.textContent='T'+tools[i].Number+' · Ø'+tools[i].Diameter+' mm');
  }
  function draw(){
    if(!tools.length){preview.innerHTML='<rect width="340" height="230" fill="#101b26"/><text x="170" y="118" text-anchor="middle" fill="#8dbbd0" font-size="13">Toque em + Adicionar para cadastrar</text>';return;}
    const t=tools[selected],safe=n=>Number.isFinite(n)&&n>0?Math.min(n,100000):1;
    const diameter=safe(t.Diameter),cut=safe(t.CuttingLength),holder=safe(t.HolderDiameter),length=safe(t.HolderLength);
    const scale=Math.min(2.5,160/(cut+length),145/Math.max(holder,diameter));
    const shaftW=Math.max(5,diameter*scale),holderW=Math.max(5,holder*scale);
    const tipY=194,cutH=Math.max(8,cut*scale),holderH=Math.max(10,length*scale),center=164;
    const shaftTop=tipY-cutH,holderTop=shaftTop-holderH;
    const kind=t.Kind||'fresa';
    const tip=kind==='broca'?'<path d="M'+(center-shaftW/2)+' '+(tipY-9)+' L'+center+' '+tipY+' L'+(center+shaftW/2)+' '+(tipY-9)+'Z" fill="#58a6ff" stroke="#d4e7f5"/>':kind==='fresa-esferica'?'<path d="M'+(center-shaftW/2)+' '+(tipY-shaftW/2)+' A'+(shaftW/2)+' '+(shaftW/2)+' 0 0 0 '+(center+shaftW/2)+' '+(tipY-shaftW/2)+'" fill="none" stroke="#d4e7f5" stroke-width="2"/>':kind==='macho'?'<path d="M'+(center-shaftW/2)+' '+(tipY-22)+' l'+shaftW+' 5 l-'+shaftW+' 5 l'+shaftW+' 5 l-'+shaftW+' 5" fill="none" stroke="#d4e7f5" stroke-width="1.5"/>':'<path d="M'+(center-shaftW/2)+' '+tipY+' H'+(center+shaftW/2)+'" stroke="#d4e7f5" stroke-width="2"/>';
    const corner=kind==='fresa-toroidal'?(()=>{const rc=Math.min(shaftW/2,Math.max(2,(+t.CornerRadius||0)*scale));return '<path d="M'+(center-shaftW/2)+' '+(tipY-rc)+' Q'+(center-shaftW/2)+' '+tipY+' '+(center-shaftW/2+rc)+' '+tipY+' H'+(center+shaftW/2-rc)+' Q'+(center+shaftW/2)+' '+tipY+' '+(center+shaftW/2)+' '+(tipY-rc)+'" fill="none" stroke="#d4e7f5" stroke-width="2"/>';})():'';
    const faceMill=kind==='fresa-facear'?'<rect x="'+(center-Math.max(shaftW,holderW)/2)+'" y="'+(tipY-11)+'" width="'+Math.max(shaftW,holderW)+'" height="11" rx="2" fill="#58a6ff" stroke="#d4e7f5"/>':'';
    const flute=kind==='fresa'||kind==='fresa-esferica'||kind==='fresa-toroidal'?Array.from({length:3},(_,i)=>'<path d="M'+(center-shaftW*.4)+' '+(shaftTop+cutH*(i+.1)/3)+' Q'+(center+shaftW*.45)+' '+(shaftTop+cutH*(i+.6)/3)+' '+(center-shaftW*.2)+' '+(shaftTop+cutH*(i+1)/3)+'" fill="none" stroke="#d9edff" stroke-width="1" opacity=".7"/>').join(''):'';
    const box=(x,y,w,h,fill)=>'<rect x="'+x+'" y="'+y+'" width="'+w+'" height="'+h+'" fill="'+fill+'" stroke="#9bb2c4" stroke-width="1.2"/>';
    const line=(x1,y1,x2,y2)=>'<path d="M'+x1+' '+y1+'L'+x2+' '+y2+'" stroke="#8dbbd0" stroke-width="1"/>';
    preview.innerHTML='<rect width="340" height="230" fill="#101b26"/><text x="12" y="20" fill="#e6edf3" font-size="13">T'+t.Number+' · Ferramenta ilustrativa</text><path d="M164 32V208" stroke="#415569" stroke-dasharray="4 5"/>'+
      '<path d="M'+(center-holderW*.34)+' '+holderTop+' H'+(center+holderW*.34)+' L'+(center+holderW/2)+' '+(holderTop+holderH*.22)+' V'+(shaftTop-holderH*.16)+' L'+(center+Math.min(holderW,shaftW*1.2)/2)+' '+shaftTop+' H'+(center-Math.min(holderW,shaftW*1.2)/2)+' L'+(center-holderW/2)+' '+(shaftTop-holderH*.16)+' V'+(holderTop+holderH*.22)+' Z" fill="#526879" stroke="#9bb2c4" stroke-width="1.2"/>'+
      box(center-shaftW/2,shaftTop,shaftW,cutH,'#58a6ff')+flute+(kind==='fresa-toroidal'?corner:tip)+faceMill+
      line(center+shaftW/2+6,shaftTop,center+shaftW/2+6,tipY)+
      line(center-holderW/2-9,holderTop,center-holderW/2-9,shaftTop)+
      '<text x="'+(center-holderW/2-14)+'" y="'+(holderTop+holderH/2)+'" text-anchor="end" fill="#8dbbd0" font-size="11">Lp '+length+'</text>'+
      '<text x="'+(center+shaftW/2+12)+'" y="'+(shaftTop+cutH/2)+'" fill="#8dbbd0" font-size="11">Lc '+cut+'</text>'+
      '<text x="12" y="216" fill="#8dbbd0" font-size="12">Corte Ø'+diameter+' · Porta-ferramenta Ø'+holder+'</text>';
  }
  function render(){
    selector.replaceChildren();
    tools.forEach((t,i)=>{const opt=document.createElement('option');opt.value=String(i);selector.append(opt);});
    selector.value=String(selected);fields.replaceChildren();
    if(!tools.length){updateLabels();draw();return;}
    toolType();numeric('Número T','Number',true);numeric('Diâmetro de corte (mm)','Diameter');
    if(tools[selected].Kind==='fresa-toroidal')numeric('Raio de canto (mm)','CornerRadius');
    if(tools[selected].Kind==='broca')numeric('Ângulo da ponta (°)','TipAngle');
    numeric('Comprimento de corte (mm)','CuttingLength');numeric('Diâmetro do porta-ferramenta (mm)','HolderDiameter');
    numeric('Comprimento do porta-ferramenta (mm)','HolderLength');
    updateLabels();draw();
  }
  render();
  return {
    validate(){
      if(tools.length>20)return 'Cadastre no máximo 20 ferramentas.';
      const nums=new Set();
      for(const t of tools){
        if(!KINDS.some(k=>k[0]===t.Kind))return 'Selecione um tipo de ferramenta válido.';
        if(!Number.isInteger(t.Number)||t.Number<1||t.Number>9999||nums.has(t.Number))return 'Números T devem ser únicos, entre 1 e 9999.';
        nums.add(t.Number);
        if(!['Diameter','CuttingLength','HolderDiameter','HolderLength'].every(k=>Number.isFinite(t[k])&&t[k]>0&&t[k]<=100000))return 'Confira as dimensões das ferramentas.';
        if(!(Number.isFinite(t.CornerRadius)&&t.CornerRadius>=0&&t.CornerRadius<=t.Diameter/2))return 'T'+t.Number+': o raio de canto vai de 0 até metade do diâmetro.';
        if(!(Number.isFinite(t.TipAngle)&&t.TipAngle>0&&t.TipAngle<180))return 'T'+t.Number+': o ângulo da ponta precisa estar entre 0° e 180°.';
      }
      return null;
    },
    save(){data.Tools=tools.map(t=>({...t}));}
  };
}
