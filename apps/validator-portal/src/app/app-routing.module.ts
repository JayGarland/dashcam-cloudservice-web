import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { AuthComponent } from './auth/auth.component';
import { AuthGuard } from './auth/auth.guard';
import { VerifyClaimComponent } from './claims/verify-claim/verify-claim.component';

const routes: Routes = [
  { path: '', redirectTo: 'claims/verify', pathMatch: 'full' },
  { path: 'login', component: AuthComponent },
  {
    path: 'claims/verify',
    component: VerifyClaimComponent,
    canActivate: [AuthGuard]
  },
  { path: '**', redirectTo: 'claims/verify' }
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule {}
