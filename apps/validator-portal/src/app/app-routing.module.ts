import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { VerifyClaimComponent } from './claims/verify-claim/verify-claim.component';

const routes: Routes = [
  { path: '', redirectTo: 'claims/verify', pathMatch: 'full' },
  { path: 'claims/verify', component: VerifyClaimComponent },
  { path: '**', redirectTo: 'claims/verify' }
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule {}
