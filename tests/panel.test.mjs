import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {Window} from 'happy-dom';
import {IDBFactory} from 'fake-indexeddb';
import {webcrypto} from 'node:crypto';
const html=await readFile(new URL('../dist/index.html',import.meta.url),'utf8');
const source=await readFile(new URL('../dist/app.js',import.meta.url),'utf8');
async function app(){const w=new Window({url:'https://bacheca.test',settings:{disableCSSFileLoading:true,disableJavaScriptFileLoading:true}});w.document.write(html.replace(/<script[^>]*>[\s\S]*?<\/script>/g,''));w.indexedDB=new IDBFactory();Object.defineProperty(w,'crypto',{value:webcrypto});w.HTMLDialogElement.prototype.showModal=function(){this.open=true;};w.HTMLDialogElement.prototype.close=function(){this.open=false;};await w.eval('(async()=>{'+source+'\nglobalThis.app={state,api,demo,render,persist,pair,db,localGet,upload};})()');return w;}
const settle=()=>new Promise(r=>setTimeout(r,25));
test('API: scritture riuscite con corpo vuoto 201/200/204 restituiscono null',async()=>{const w=await app();try{
 for(const status of [200,201,204]){w.fetch=async()=>new Response(null,{status});assert.equal(await w.app.api('/rest/v1/slides',{method:'POST',body:{title:'test'}}),null);}
}finally{await w.happyDOM.close();}});
test('API: risposta JSON non valida non viene classificata come rifiuto del salvataggio',async()=>{const w=await app();try{
 w.fetch=async()=>new Response('{',{status:201});
 await assert.rejects(w.app.api('/rest/v1/slides',{method:'POST',body:{title:'test'}}),e=>e.status===undefined&&/verificare il salvataggio/.test(e.message));
}finally{await w.happyDOM.close();}});
test('caricamento cloud: POST 201 vuoto salva avviso e immagine senza cancellarla',async()=>{const w=await app();try{
 w.app.state.demo=false;w.app.state.board={id:'board-test',name:'Condominio'};w.app.state.slides=[];
 w.createImageBitmap=async()=>({width:800,height:600,close(){}});
 w.HTMLCanvasElement.prototype.getContext=()=>({fillRect(){},drawImage(){}});
 w.HTMLCanvasElement.prototype.toBlob=function(cb){cb(new w.Blob(['jpeg-fixture'],{type:'image/jpeg'}));};
 const calls=[];let row;
 w.fetch=async(url,opts)=>{calls.push({url,opts});
  if(url.includes('/rest/v1/slides')&&opts.method==='POST'){row=JSON.parse(opts.body);return new Response(null,{status:201});}
  if(url.includes('/storage/v1/object/sign/'))return new Response(JSON.stringify({signedURL:'/object/sign/notices/test.jpg?token=test'}));
  if(url.includes('/storage/v1/object/notices/'))return new Response(JSON.stringify({Key:'notices/test.jpg'}));
  if(url.includes('/rest/v1/slides?'))return new Response(JSON.stringify([row]));
  if(url.includes('/rest/v1/devices?'))return new Response('[]');
  throw new Error('Unexpected request '+url);
 };
 await w.app.upload([new w.File(['image'],'avviso.jpg',{type:'image/jpeg'})]);
 assert.ok(row);assert.equal(w.app.state.slides.length,1);assert.equal(w.app.state.slides[0].title,'avviso');
 assert.equal(calls.filter(c=>c.opts.method==='DELETE').length,0);
 assert.match(w.document.querySelector('#toast').textContent,/1 immagini aggiunte/);
}finally{await w.happyDOM.close();}});
test('demo: ordine, pausa, rinomina ed eliminazione persistono senza cloud',async()=>{const w=await app();try{const d=w.document;assert.equal(d.querySelectorAll('.notice-row').length,3);assert.equal(d.querySelector('#totalStat').textContent,'3');d.querySelector('[data-action="down"]').click();await settle();assert.equal(w.app.state.slides[0].id,'sample2');d.querySelector('[data-action="toggle"]').click();await settle();assert.equal(d.querySelector('#totalStat').textContent,'2');d.querySelector('[data-action="rename"]').click();d.querySelector('#renameTitle').value='<img src=x onerror=alert(1)>';d.querySelector('#renameForm').dispatchEvent(new w.Event('submit',{cancelable:true}));await settle();assert.equal(d.querySelector('.notice-info strong').textContent,'<img src=x onerror=alert(1)>');assert.equal(d.querySelector('.notice-info strong img'),null);await w.app.demo();assert.equal(w.app.state.slides[0].active,false);assert.match(w.app.state.slides[0].title,/onerror/);d.querySelector('[data-action="delete"]').click();d.querySelector('#confirmDelete').click();await settle();assert.equal(w.app.state.slides.length,2);await w.app.demo();assert.equal(w.app.state.slides.length,2);}finally{await w.happyDOM.close();}});
test('demo: anteprima, navigazione e pairing non inventano dispositivi reali',async()=>{const w=await app();try{const d=w.document;d.querySelector('#previewButton').click();assert.match(d.querySelector('#largePreview').src,/data:image\/svg/);d.querySelector('#nextPreview').click();assert.equal(d.querySelector('#largeCounter').textContent,'2 / 3');d.querySelector('#closeModal').click();d.querySelector('[data-view="schermi"]').click();assert.equal(d.querySelector('#schermiView').hidden,false);assert.equal(d.querySelector('#avvisiView').hidden,true);d.querySelector('#pairButton').click();assert.ok(d.querySelector('#loginForm'));assert.equal(w.app.state.devices.length,0);}finally{await w.happyDOM.close();}});
test('API: rinnovo sessione prima della richiesta, nessuna chiave segreta necessaria',async()=>{const w=await app();try{w.app.state.config={url:'https://example.supabase.co',key:'sb_publishable_test'};w.app.state.session={expires_at:0,access_token:'old',refresh_token:'refresh'};const calls=[];w.fetch=async(url,opts)=>{calls.push({url,opts});return new Response(JSON.stringify(url.includes('/auth/')?{access_token:'new',refresh_token:'new-refresh',expires_in:3600}:{ok:true}),{status:200});};const value=await w.app.api('/rest/v1/boards');assert.equal(value.ok,true);assert.equal(calls.length,2);assert.match(calls[0].url,/grant_type=refresh_token/);assert.equal(calls[1].opts.headers.Authorization,'Bearer new');assert.equal(calls[1].opts.headers.apikey,'sb_publishable_test');}finally{await w.happyDOM.close();}});
test('API: errore cloud viene propagato, non presentato come successo',async()=>{const w=await app();try{w.app.state.config={url:'https://example.supabase.co',key:'public'};w.fetch=async()=>new Response(JSON.stringify({message:'Access denied'}),{status:403});await assert.rejects(w.app.api('/rest/v1/slides'),/Access denied/);}finally{await w.happyDOM.close();}});
