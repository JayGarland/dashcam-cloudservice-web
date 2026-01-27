import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { HttpClientModule } from '@angular/common/http';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { VerifyClaimModule } from './claims/verify-claim/verify-claim.module';
import { AuthModule } from './auth/auth.module';
import { AUTH_TOKEN_PROVIDER } from './api/validator-api.service';
import { AuthService } from './auth/auth.service';

@NgModule({
  declarations: [AppComponent],
  imports: [BrowserModule, HttpClientModule, AppRoutingModule, VerifyClaimModule, AuthModule],
  providers: [
    {
      provide: AUTH_TOKEN_PROVIDER,
      useFactory: (auth: AuthService) => () => auth.getAccessTokenSync(),
      deps: [AuthService]
    }
  ],
  bootstrap: [AppComponent]
})
export class AppModule {}
